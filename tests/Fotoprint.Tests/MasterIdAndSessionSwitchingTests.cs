using Fotoprint.Core.Agents;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Models;
using Xunit;

namespace Fotoprint.Tests;

public class MasterIdAndSessionSwitchingTests
{
    private class InMemoryDialogDb
    {
        public List<Employee> Employees { get; } = new()
        {
            new() { Id = 1, MasterId = 79001000001, TelegramId = 100001, FirstName = "Алексей", LastName = "Смирнов" },
            new() { Id = 2, MasterId = 79001000002, TelegramId = 100002, FirstName = "Иван", LastName = "Кузнецов" }
        };

        public List<ChatSession> Sessions { get; } = new();
        public List<ChatMessage> Messages { get; } = new();

        public long? ResolveMasterId(string messenger, string messengerUserId)
        {
            if (messenger == "telegram" && long.TryParse(messengerUserId, out var tgId))
            {
                return Employees.FirstOrDefault(e => e.TelegramId == tgId)?.MasterId;
            }
            return null;
        }

        public ChatSession GetOrCreateActiveSession(long masterId, long chatId)
        {
            var active = Sessions.FirstOrDefault(s => s.MasterId == masterId && s.IsActive);
            if (active != null) return active;

            var newSession = new ChatSession
            {
                Id = Guid.NewGuid(),
                MasterId = masterId,
                MessengerChatId = chatId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            Sessions.Add(newSession);
            return newSession;
        }

        public ChatSession SwitchToNewSession(Guid currentSessionId, long masterId, long chatId, string newBranch)
        {
            var old = Sessions.FirstOrDefault(s => s.Id == currentSessionId);
            if (old != null)
            {
                old.IsActive = false;
                old.ClosedAt = DateTime.UtcNow;
            }

            foreach (var m in Messages.Where(m => m.SessionId == currentSessionId))
            {
                m.IsFrozen = true;
            }

            var newSession = new ChatSession
            {
                Id = Guid.NewGuid(),
                MasterId = masterId,
                MessengerChatId = chatId,
                CurrentBranch = newBranch,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            Sessions.Add(newSession);
            return newSession;
        }

        public void SaveMessage(Guid sessionId, long masterId, string role, string content)
        {
            Messages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                MasterId = masterId,
                Role = role,
                Content = content,
                IsFrozen = false,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    [Fact]
    public void MasterId_Is_Numeric_Only_And_Resolved_From_TelegramId()
    {
        var db = new InMemoryDialogDb();

        var masterId = db.ResolveMasterId("telegram", "100001");

        Assert.NotNull(masterId);
        Assert.Equal(79001000001, masterId.Value);

        // Проверяем что содержит строго только цифры
        var masterIdStr = masterId.Value.ToString();
        Assert.All(masterIdStr, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void SessionId_Changes_When_ContextSwitch_Occurs()
    {
        var db = new InMemoryDialogDb();
        long masterId = 79001000001;
        long chatId = 999888;

        // 1. Первая активная сессия пользователя
        var initialSession = db.GetOrCreateActiveSession(masterId, chatId);
        var initialSessionId = initialSession.Id;

        db.SaveMessage(initialSessionId, masterId, "user", "Какая норма брака на офсетной машине Heidelberg?");
        db.SaveMessage(initialSessionId, masterId, "assistant", "Норма расхода и брака по ТР-04 составляет до 1.5%.");

        // Проверяем что сессия активна и содержит сообщения
        var session1Messages = db.Messages.Where(m => m.SessionId == initialSessionId).ToList();
        Assert.Equal(2, session1Messages.Count);
        Assert.True(initialSession.IsActive);

        // 2. Пользователь меняет контекст на совершенно другую тему (например, ставит задачу в CRM)
        // SwitchDetector определяет смену контекста
        var switchResult = new ContextSwitchResult(
            IsSwitched: true,
            SuggestedBranch: "task_management",
            Reason: "Пользователь переключился на постановку задачи в Битрикс24");

        // 3. Выполняется смена сессии (SessionId меняется!)
        ChatSession newSession;
        if (switchResult.IsSwitched)
        {
            newSession = db.SwitchToNewSession(initialSessionId, masterId, chatId, switchResult.SuggestedBranch);
        }
        else
        {
            newSession = initialSession;
        }

        // Проверяем инварианты смены диалога:
        // А. SessionId гарантированно изменился
        Assert.NotEqual(initialSessionId, newSession.Id);

        // Б. Старая сессия закрыта и помечена неактивной
        Assert.False(initialSession.IsActive);
        Assert.NotNull(initialSession.ClosedAt);

        // В. Старые сообщения заморожены
        Assert.All(db.Messages.Where(m => m.SessionId == initialSessionId), m => Assert.True(m.IsFrozen));

        // Г. Новая сессия активна и привязана к тому же MasterId
        Assert.True(newSession.IsActive);
        Assert.Equal(masterId, newSession.MasterId);
        Assert.Equal("task_management", newSession.CurrentBranch);

        // Д. Новое сообщение пишется уже в новый SessionId
        db.SaveMessage(newSession.Id, masterId, "user", "Создай задачу Кузнецову проверить наладку офсета");

        var session2Messages = db.Messages.Where(m => m.SessionId == newSession.Id).ToList();
        Assert.Single(session2Messages);
        Assert.Equal("user", session2Messages[0].Role);
        Assert.False(session2Messages[0].IsFrozen);
    }
}

