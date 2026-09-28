namespace ParentalGuard.Service.Config;

public enum OverlayMessageValidationResult
{
    Valid,
    TooLong,
    InvalidCharacters,
}

/// <summary>
/// `FE-012`/`FE-012a` (`Specification/03-frontend-ui-spec.md` v0.6.0) — Service KHÔNG tin validate
/// phía UI (defense in depth, `ConfigUpdateResult.INVALID_CHARACTERS`/`TOO_LONG` tồn tại đúng vì lý
/// do này). Hàm thuần, test được không cần IPC/DB thật.
/// </summary>
public static class OverlayMessageValidator
{
    public const int MaxLength = 255;

    private const string _allowedPunctuation = ".,!?:;-()\"'";

    public static OverlayMessageValidationResult Validate(string message)
    {
        if (message.Length > MaxLength)
        {
            return OverlayMessageValidationResult.TooLong;
        }

        foreach (char c in message)
        {
            if (!IsAllowedChar(c))
            {
                return OverlayMessageValidationResult.InvalidCharacters;
            }
        }

        return OverlayMessageValidationResult.Valid;
    }

    /// <summary>Kiểm tra `IsControl` TRƯỚC `IsWhiteSpace` — 1 số ký tự điều khiển (tab/newline) cũng khớp `IsWhiteSpace`, phải bị cấm theo đúng nghĩa đen `FE-012a` ("ký tự điều khiển").</summary>
    private static bool IsAllowedChar(char c)
    {
        if (char.IsControl(c))
        {
            return false;
        }

        if (char.IsWhiteSpace(c) || char.IsLetterOrDigit(c))
        {
            return true;
        }

        return _allowedPunctuation.Contains(c);
    }
}
