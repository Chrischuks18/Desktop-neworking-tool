using System.Globalization;
namespace OfficeNetwork.Server.Services;

internal static class RegisterValidation
{
    public static void Text(string? value,string label,int maximum,bool required)
    {
        if(value is null || value.Length>maximum || required && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{label} {(required?"is required and ":"")}must not exceed {maximum} characters.");
    }
    public static void Date(string? value,string label,bool required)
    {
        if(!required && string.IsNullOrWhiteSpace(value))return;
        if(!DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            throw new ArgumentException($"{label} must be a valid date (YYYY-MM-DD).");
    }
}
