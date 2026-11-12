using Fotoprint.Core.Data;
using Fotoprint.Core.Models;
using Fotoprint.Core.Security;
using Xunit;

namespace Fotoprint.Tests;

public class EmployeeSearchTests
{
    private static List<User> CreateTestEmployees()
    {
        return new List<User>
        {
            new()
            {
                Id = 1,
                MasterId = 100001,
                FirstName = "Алексей",
                LastName = "Смирнов",
                MiddleName = "Сергеевич",
                Email = "a.smirnov@fotoprint.ru",
                TelegramId = 100001,
                Department = "Руководство",
                Position = "Генеральный директор",
                IsActive = true
            },
            new()
            {
                Id = 2,
                MasterId = 100002,
                FirstName = "Иван",
                LastName = "Кузнецов",
                MiddleName = "Петрович",
                Email = "i.kuznetsov@fotoprint.ru",
                TelegramId = 100002,
                Department = "Производство",
                Position = "Начальник производства",
                IsActive = true
            },
            new()
            {
                Id = 3,
                MasterId = 100003,
                FirstName = "Елена",
                LastName = "Морозова",
                MiddleName = "Алексеевна",
                Email = "e.morozova@fotoprint.ru",
                TelegramId = 100003,
                Department = "Продажи",
                Position = "Руководитель отдела продаж",
                IsActive = true
            },
            new()
            {
                Id = 4,
                MasterId = 100004,
                FirstName = "Ольга",
                LastName = "Соколова",
                MiddleName = "Викторовна",
                Email = "o.sokolova@fotoprint.ru",
                TelegramId = 100004,
                Department = "Бухгалтерия",
                Position = "Главный бухгалтер",
                IsActive = true
            },
            new()
            {
                Id = 5,
                MasterId = 100005,
                FirstName = "Фёдор",
                LastName = "Семёнов",
                MiddleName = "Игоревич",
                Email = "f.semenov@fotoprint.ru",
                TelegramId = 100005,
                Department = "Производство",
                Position = "Мастер цеха",
                IsActive = true
            },
            new()
            {
                Id = 6,
                MasterId = 100006,
                FirstName = "Дмитрий",
                LastName = "Уволенный",
                MiddleName = "Иванович",
                Email = "d.uvolen@fotoprint.ru",
                TelegramId = 100006,
                Department = "Склад",
                Position = "Кладовщик",
                IsActive = false
            }
        };
    }

    [Fact]
    public void OrderIndependent_FIO_Tokens_Return_Same_Top_Match()
    {
        var employees = CreateTestEmployees();

        var query1 = "Алексей Смирнов";
        var query2 = "Смирнов Алексей";
        var query3 = "Сергеевич Смирнов Алексей";

        var res1 = FotoprintRepository.RankEmployees(employees, query1);
        var res2 = FotoprintRepository.RankEmployees(employees, query2);
        var res3 = FotoprintRepository.RankEmployees(employees, query3);

        Assert.NotEmpty(res1);
        Assert.NotEmpty(res2);
        Assert.NotEmpty(res3);

        Assert.Equal(1, res1.First().Id);
        Assert.Equal(1, res2.First().Id);
        Assert.Equal(1, res3.First().Id);

        Assert.Equal("Смирнов Алексей Сергеевич", res1.First().FullName);
        Assert.Equal("Смирнов Алексей Сергеевич", res2.First().FullName);
        Assert.Equal("Смирнов Алексей Сергеевич", res3.First().FullName);
    }

    [Fact]
    public void Role_And_Department_Search_Finds_Expected_Employee()
    {
        var employees = CreateTestEmployees();

        // 1. Поиск по должности "начальник производства"
        var prodResults = FotoprintRepository.RankEmployees(employees, "начальник производства");
        Assert.NotEmpty(prodResults);
        Assert.Equal(2, prodResults.First().Id);
        Assert.Equal("Кузнецов Иван Петрович", prodResults.First().FullName);
        Assert.Equal("Начальник производства", prodResults.First().Position);

        // 2. Поиск по должности "Руководитель отдела продаж"
        var salesResults = FotoprintRepository.RankEmployees(employees, "руководитель отдела продаж");
        Assert.NotEmpty(salesResults);
        Assert.Equal(3, salesResults.First().Id);
        Assert.Equal("Морозова Елена Алексеевна", salesResults.First().FullName);

        // 3. Поиск по отделу "Бухгалтерия"
        var accResults = FotoprintRepository.RankEmployees(employees, "бухгалтерия");
        Assert.NotEmpty(accResults);
        Assert.Equal(4, accResults.First().Id);
        Assert.Equal("Соколова Ольга Викторовна", accResults.First().FullName);

        // 4. Комбинированный поиск "директор Смирнов"
        var ceoResults = FotoprintRepository.RankEmployees(employees, "директор Смирнов");
        Assert.NotEmpty(ceoResults);
        Assert.Equal(1, ceoResults.First().Id);
    }

    [Fact]
    public void Empty_Or_Whitespace_Query_Returns_Active_Employees_Alphabetically()
    {
        var employees = CreateTestEmployees();

        var resEmpty = FotoprintRepository.RankEmployees(employees, "");
        var resWhitespace = FotoprintRepository.RankEmployees(employees, "   \t\n  ");

        Assert.Equal(5, resEmpty.Count);
        Assert.Equal(5, resWhitespace.Count);

        // Исключен неактивный сотрудник ID 6
        Assert.DoesNotContain(resEmpty, e => e.Id == 6);
        Assert.DoesNotContain(resWhitespace, e => e.Id == 6);

        // Проверяем сортировку по фамилии
        var lastNames = resEmpty.Select(e => e.LastName).ToList();
        Assert.Equal(new[] { "Кузнецов", "Морозова", "Семёнов", "Смирнов", "Соколова" }, lastNames);
    }

    [Fact]
    public void Yo_Normalization_Matches_Equivalently()
    {
        var employees = CreateTestEmployees();

        // Запрос с "е" находит запись с "ё"
        var res1 = FotoprintRepository.RankEmployees(employees, "Семенов Федор");
        Assert.NotEmpty(res1);
        Assert.Equal(5, res1.First().Id);
        Assert.Equal("Семёнов Фёдор Игоревич", res1.First().FullName);

        // Запрос с "ё" находит запись с "е"
        var res2 = FotoprintRepository.RankEmployees(employees, "Кузнёцов");
        Assert.NotEmpty(res2);
        Assert.Equal(2, res2.First().Id);
        Assert.Equal("Кузнецов Иван Петрович", res2.First().FullName);

        // Запрос "Алексëй" находит "Алексей"
        var res3 = FotoprintRepository.RankEmployees(employees, "Алексëй");
        Assert.NotEmpty(res3);
        Assert.Equal(1, res3.First().Id);
    }

    [Fact]
    public void Prefix_Match_Scores_Higher_Than_Substring_Match()
    {
        var employees = new List<User>
        {
            new()
            {
                Id = 10,
                FirstName = "Антон",
                LastName = "Смирнов",
                Position = "Менеджер",
                Department = "Продажи",
                IsActive = true
            },
            new()
            {
                Id = 11,
                FirstName = "Сергей",
                LastName = "Петров",
                Position = "Ассистент Смирнова",
                Department = "Продажи",
                IsActive = true
            }
        };

        var results = FotoprintRepository.RankEmployees(employees, "Смир");
        Assert.Equal(2, results.Count);
        // Смирнов (prefix match на LastName -> 85) должен быть выше, чем Петров (substring match в Position -> 25)
        Assert.Equal(10, results[0].Id);
        Assert.Equal(11, results[1].Id);
    }

    [Fact]
    public void All_Tokens_Must_Match_Excludes_Partial_Matches()
    {
        var employees = CreateTestEmployees();

        // Нет сотрудника, у которого одновременно есть "Алексей" и "Кузнецов"
        var results = FotoprintRepository.RankEmployees(employees, "Алексей Кузнецов");
        Assert.Empty(results);
    }

    [Fact]
    public void Limit_Is_Clamped_Between_1_And_50()
    {
        var employees = CreateTestEmployees();

        var resClampedMin = FotoprintRepository.RankEmployees(employees, "", limit: -5);
        Assert.Single(resClampedMin);

        var resClampedTwo = FotoprintRepository.RankEmployees(employees, "", limit: 2);
        Assert.Equal(2, resClampedTwo.Count);

        var resClampedMax = FotoprintRepository.RankEmployees(employees, "", limit: 100);
        Assert.Equal(5, resClampedMax.Count);
    }

    [Fact]
    public void FormatShortName_Produces_Correct_Russian_Initials()
    {
        Assert.Equal("Смирнов А.С.", User.FormatShortName("Смирнов", "Алексей", "Сергеевич"));
        Assert.Equal("Кузнецов И.П.", User.FormatShortName("Кузнецов", "Иван", "Петрович"));
        Assert.Equal("Морозова Е.А.", User.FormatShortName("Морозова", "Елена", "Алексеевна"));
        Assert.Equal("Соколова О.В.", User.FormatShortName("Соколова", "Ольга", "Викторовна"));
        Assert.Equal("Иванов И.", User.FormatShortName("Иванов", "Иван", null));
        Assert.Equal("Иванов И.", User.FormatShortName("Иванов", "Иван", "   "));
        Assert.Equal("Петров", User.FormatShortName("Петров", null, null));
        Assert.Equal("Алексей", User.FormatShortName(null, "Алексей", null));
        Assert.Equal(string.Empty, User.FormatShortName("", "", ""));
    }

    [Fact]
    public void User_Class_Exposes_ShortName_And_FromEmployee_Mapping()
    {
        var emp = new Employee
        {
            Id = 42,
            MasterId = 100042,
            FirstName = "Михаил",
            LastName = "Булгаков",
            MiddleName = "Афанасьевич",
            Department = "Литература",
            Position = "Писатель",
            IsActive = true
        };

        var user = User.FromEmployee(emp);
        Assert.Equal(42, user.Id);
        Assert.Equal(100042, user.MasterId);
        Assert.Equal("Булгаков Михаил Афанасьевич", user.FullName);
        Assert.Equal("Булгаков М.А.", user.ShortName);
    }

    private class InMemorySearchRepository : IFotoprintRepository
    {
        private readonly List<User> _users;

        public InMemorySearchRepository(List<User> users) => _users = users;

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextAsync(long telegramId, CancellationToken ct = default)
            => Task.FromResult<EmployeeSecurityContext?>(null);

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextByMasterIdAsync(long masterId, CancellationToken ct = default)
            => Task.FromResult<EmployeeSecurityContext?>(null);

        public Task<long?> ResolveMasterIdByMessengerAsync(string messengerType, string messengerUserId, CancellationToken ct = default)
            => Task.FromResult<long?>(null);

        public Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult<Employee?>(_users.FirstOrDefault(u => u.Id == id));

        public Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default)
            => Task.FromResult(_users.Cast<Employee>().ToList());

        public Task<IReadOnlyList<User>> SearchEmployeesAsync(string query, int limit = 10, CancellationToken ct = default)
        {
            var ranked = FotoprintRepository.RankEmployees(_users, query, limit);
            return Task.FromResult<IReadOnlyList<User>>(ranked);
        }

        public Task<ChatSession> GetOrCreateActiveSessionByMasterIdAsync(long masterId, long messengerChatId, string messengerType = "telegram", CancellationToken ct = default)
            => Task.FromResult(new ChatSession());

        public Task<ChatSession> SwitchToNewSessionAsync(Guid currentSessionId, long masterId, long messengerChatId, string? newBranch = null, string messengerType = "telegram", CancellationToken ct = default)
            => Task.FromResult(new ChatSession());

        public Task<List<ChatMessage>> GetMessagesBySessionIdAsync(Guid sessionId, int limit = 20, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        public Task SaveMessageToSessionAsync(Guid sessionId, long masterId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ChatSession> GetOrCreateSessionAsync(long telegramChatId, long? telegramUserId = null, CancellationToken ct = default)
            => Task.FromResult(new ChatSession());

        public Task SaveMessageAsync(Guid sessionId, string contextId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<List<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit = 20, CancellationToken ct = default)
            => Task.FromResult(new List<ChatMessage>());

        public Task FreezeContextAsync(Guid sessionId, string contextId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Guid> SaveDocumentAsync(string title, string sourceType, string? filePath, string? department, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task SaveDocumentChunksAsync(Guid documentId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<List<CorporateDocumentChunk>> SearchSimilarChunksAsync(float[] queryEmbedding, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default)
            => Task.FromResult(new List<CorporateDocumentChunk>());

        public Task<List<CorporateDocumentChunk>> SearchTextChunksAsync(string queryText, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default)
            => Task.FromResult(new List<CorporateDocumentChunk>());

        public Task<List<Meeting>> GetUpcomingMeetingsAsync(int timeWindowMinutes = 30, CancellationToken ct = default)
            => Task.FromResult(new List<Meeting>());

        public Task<Meeting?> GetMeetingByIdAsync(Guid meetingId, CancellationToken ct = default)
            => Task.FromResult<Meeting?>(null);

        public Task<List<MeetingNote>> GetMeetingNotesAsync(Guid meetingId, CancellationToken ct = default)
            => Task.FromResult(new List<MeetingNote>());

        public Task SaveMeetingNoteAsync(Guid meetingId, int? authorId, string noteText, string category = "general", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Guid> SaveMeetingProtocolAsync(Guid meetingId, string title, string summary, List<string> decisions, string? rawTranscript = null, string? docxPath = null, string? pdfPath = null, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<MeetingProtocol?> GetMeetingProtocolAsync(Guid meetingId, CancellationToken ct = default)
            => Task.FromResult<MeetingProtocol?>(null);

        public Task<Guid> CreateActionItemAsync(Guid meetingId, Guid? protocolId, string title, string? description, int? responsibleId, DateTime? deadline, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<List<ActionItem>> GetActionItemsByMeetingAsync(Guid meetingId, CancellationToken ct = default)
            => Task.FromResult(new List<ActionItem>());

        public Task<List<ActionItem>> GetActionItemsByResponsibleAsync(int employeeId, string? statusFilter = null, CancellationToken ct = default)
            => Task.FromResult(new List<ActionItem>());

        public Task UpdateActionItemStatusAsync(Guid actionItemId, string status, int? bitrix24TaskId = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    [Fact]
    public void Employee_Search_Projection_Matches_Contract_Shape()
    {
        var emp = new User
        {
            Id = 1,
            MasterId = 100001,
            FirstName = "Алексей",
            LastName = "Смирнов",
            MiddleName = "Сергеевич",
            Email = "a.smirnov@fotoprint.ru",
            TelegramId = 100001,
            Department = "Руководство",
            Position = "Генеральный директор",
            IsActive = true
        };

        var shortName = User.FormatShortName(emp.LastName, emp.FirstName, emp.MiddleName);
        var item = new
        {
            employee_id = emp.Id,
            telegram_id = emp.TelegramId,
            full_name = emp.FullName,
            short_name = shortName,
            department = emp.Department,
            position = emp.Position,
            roles = new List<string> { "Director" }
        };

        var response = new
        {
            employees = new[] { item },
            total_matched = 1
        };

        var json = System.Text.Json.JsonSerializer.Serialize(response);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("employees", out var empArr));
        Assert.True(root.TryGetProperty("total_matched", out var totalMatched));
        Assert.Equal(1, totalMatched.GetInt32());

        var firstEmp = empArr[0];
        Assert.Equal(1, firstEmp.GetProperty("employee_id").GetInt32());
        Assert.Equal(100001, firstEmp.GetProperty("telegram_id").GetInt64());
        Assert.Equal("Смирнов Алексей Сергеевич", firstEmp.GetProperty("full_name").GetString());
        Assert.Equal("Смирнов А.С.", firstEmp.GetProperty("short_name").GetString());
        Assert.Equal("Руководство", firstEmp.GetProperty("department").GetString());
        Assert.Equal("Генеральный директор", firstEmp.GetProperty("position").GetString());
        Assert.Equal("Director", firstEmp.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Repository_SearchEmployeesAsync_Contract_Returns_Expected_Result()
    {
        var repo = new InMemorySearchRepository(CreateTestEmployees());

        var searchResults = await repo.SearchEmployeesAsync("Смирнов Алексей", limit: 5);
        Assert.NotEmpty(searchResults);
        Assert.Equal(1, searchResults[0].Id);
        Assert.Equal("Смирнов Алексей Сергеевич", searchResults[0].FullName);
        Assert.Equal("Смирнов А.С.", searchResults[0].ShortName);
    }
}
