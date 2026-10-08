using System.Security.Cryptography;

namespace ParentalGuard.UI.Game;

/// <summary>
/// `PAUSE-049` (2026-10-08): đếm số lần THUA liên tiếp (vấp rào / rơi hố — thoát trò chơi không tính). Khi đạt ngưỡng ngẫu nhiên 5–10 lần
/// thì gợi ý "Hay là không tạm dừng nữa?" + nút Donate, rồi bốc ngưỡng mới và đếm lại. Về đích thì đếm lại từ đầu.
/// Chỉ sống trong phiên Dashboard (không lưu) — thuần trải nghiệm, không phải cơ chế bảo vệ.
/// </summary>
public sealed class LossStreakTracker(Func<int, int, int>? nextInt = null)
{
    public const int MinThreshold = 5;
    public const int MaxThreshold = 10;

    private readonly Func<int, int, int> _nextInt = nextInt ?? RandomNumberGenerator.GetInt32;
    private int _losses;
    private int _threshold = -1;

    public int ConsecutiveLosses => _losses;

    /// <summary>Ghi 1 lần thua; <c>true</c> = lần này hiện gợi ý thôi tạm dừng.</summary>
    public bool RecordLoss()
    {
        if (_threshold < 0)
        {
            _threshold = _nextInt(MinThreshold, MaxThreshold + 1);
        }

        _losses++;
        if (_losses < _threshold)
        {
            return false;
        }

        _losses = 0;
        _threshold = _nextInt(MinThreshold, MaxThreshold + 1);
        return true;
    }

    public void RecordWin() => _losses = 0;
}
