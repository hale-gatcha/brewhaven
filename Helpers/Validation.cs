using System.Text.RegularExpressions;

namespace BrewHaven.Helpers;

public static partial class Validation
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailRegex();
    [GeneratedRegex(@"^\d{11}$")] private static partial Regex PhilippineMobileRegex();

    public static bool IsValidEmail(string email) => EmailRegex().IsMatch(email.Trim());
    [GeneratedRegex(@"^09\d{9}$")] private static partial Regex PhMobileRegex();
    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,50}$")] private static partial Regex UsernameRegex();

    public static bool IsValidContact(string contact) => PhilippineMobileRegex().IsMatch(contact.Trim());
    /// <summary>11-digit Philippine mobile number, e.g. 09171234567.</summary>
    public static bool IsValidPhMobile(string contact) => PhMobileRegex().IsMatch(contact.Trim());
    public static bool IsValidUsername(string username) => UsernameRegex().IsMatch(username.Trim());
}
