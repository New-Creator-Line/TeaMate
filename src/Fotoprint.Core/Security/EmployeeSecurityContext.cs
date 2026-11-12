namespace Fotoprint.Core.Security;

public class EmployeeSecurityContext
{
    public int EmployeeId { get; set; }
    public long MasterId { get; set; } // Уникальный цифровой MasterId (только цифры)
    public string FullName { get; set; } = string.Empty;
    public long TelegramId { get; set; }
    public string? TelegramUsername { get; set; }
    public string Department { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<string> AccessGroups { get; set; } = new();

    public bool HasPermission(string permissionCode)
    {
        if (Roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
            return true;

        return Permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasAccessGroup(string documentRequiredGroup)
    {
        if (Roles.Contains("Director", StringComparer.OrdinalIgnoreCase) ||
            AccessGroups.Contains("c-level", StringComparer.OrdinalIgnoreCase))
            return true;

        if (string.Equals(documentRequiredGroup, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return AccessGroups.Contains(documentRequiredGroup, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsExecutive =>
        Roles.Contains("Director", StringComparer.OrdinalIgnoreCase) ||
        AccessGroups.Contains("c-level", StringComparer.OrdinalIgnoreCase);
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute
{
    public string Permission { get; }

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireConfirmationAttribute : Attribute
{
    public bool Required { get; }

    public RequireConfirmationAttribute(bool required = true)
    {
        Required = required;
    }
}

