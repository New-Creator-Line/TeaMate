using Fotoprint.Core.Security;
using Xunit;

namespace Fotoprint.Tests;

public class SecurityContextTests
{
    [Fact]
    public void Director_Has_All_Permissions_And_Executive_Flag()
    {
        var directorContext = new EmployeeSecurityContext
        {
            EmployeeId = 1,
            FullName = "Смирнов Алексей",
            TelegramId = 100001,
            Department = "Руководство",
            Roles = new List<string> { "Director" },
            Permissions = new List<string> { "calendar:read_meetings" },
            AccessGroups = new List<string> { "c-level", "all" }
        };

        Assert.True(directorContext.IsExecutive);
        Assert.True(directorContext.HasPermission("onec:read_orders"));
        Assert.True(directorContext.HasPermission("onec:read_production_kpis"));
        Assert.True(directorContext.HasPermission("executive:view_summary"));
        Assert.True(directorContext.HasAccessGroup("accounting"));
    }

    [Fact]
    public void Production_Staff_Can_Access_Only_Granted_Permissions()
    {
        var prodContext = new EmployeeSecurityContext
        {
            EmployeeId = 2,
            FullName = "Кузнецов Иван",
            TelegramId = 100002,
            Department = "Производство",
            Roles = new List<string> { "Production" },
            Permissions = new List<string> { "onec:read_orders", "onec:read_production_kpis", "bitrix24:read_tasks" },
            AccessGroups = new List<string> { "all", "production" }
        };

        Assert.False(prodContext.IsExecutive);
        Assert.True(prodContext.HasPermission("onec:read_orders"));
        Assert.True(prodContext.HasPermission("onec:read_production_kpis"));
        Assert.False(prodContext.HasPermission("executive:view_summary")); // Не имеет права на Executive Summary
        Assert.True(prodContext.HasAccessGroup("production"));
        Assert.False(prodContext.HasAccessGroup("accounting")); // Не имеет доступа к финансовым документам
    }
}

