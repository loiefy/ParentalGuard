using System.Drawing;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace ParentalGuard.Overlay.Windows;

/// <summary>
/// Lớp 1 — tra cứu nút đóng thật qua <c>IUIAutomation</c> (`FE-016c`, Architecture/07-overlay-architecture.md
/// mục 2.2, ADR-53/55). Raw COM interop qua <c>UIAutomationClient.dll</c> (package
/// <c>Interop.UIAutomationClient</c>, sinh bởi tlbimp từ chính type library COM — KHÔNG phải
/// assembly managed <c>System.Windows.Automation</c> của WPF), đúng quyết định kiến trúc đã chốt.
/// </summary>
public static class CloseButtonLocator
{
    /// <summary>`FE-016c`, đã "ĐÃ CHỐT CHÍNH THỨC v0.4.2" — đo từ lúc bắt đầu <c>FindAll</c> tới lúc có kết quả.</summary>
    public const int TimeoutMs = 150;

    /// <summary>
    /// Trả <c>null</c> nếu timeout hoặc không tìm thấy — caller (mục 2.1) giữ nguyên lớp 3 trong
    /// trường hợp đó. ADR-53: thực thi trên 1 <see cref="Thread"/> STA riêng mỗi lần gọi (không
    /// <c>Task.Run</c>/ThreadPool) vì COM call block đồng bộ, không cancel được giữa chừng — nếu
    /// timeout thắng, thread STA có thể vẫn treo và tự thoát muộn sau đó (không ảnh hưởng, kết quả
    /// muộn bị bỏ qua đúng mục 2.2 "không áp dụng, tránh giật hình do nâng cấp trễ vô nghĩa").
    /// </summary>
    public static async Task<Rectangle?> LookupAsync(IntPtr hwnd, Rectangle windowRect)
    {
        var tcs = new TaskCompletionSource<Rectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunLookup(hwnd, windowRect, tcs))
        {
            IsBackground = true,
            Name = "ParentalGuard.Overlay.UiaLookup",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Task first = await Task.WhenAny(tcs.Task, Task.Delay(TimeoutMs)).ConfigureAwait(false);
        return ReferenceEquals(first, tcs.Task) ? await tcs.Task.ConfigureAwait(false) : null;
    }

    private static void RunLookup(IntPtr hwnd, Rectangle windowRect, TaskCompletionSource<Rectangle?> tcs)
    {
        try
        {
            var automation = (IUIAutomation)new CUIAutomationClass();
            IUIAutomationElement root = automation.ElementFromHandle(hwnd);
            if (root is null)
            {
                tcs.TrySetResult(null);
                return;
            }

            IUIAutomationCondition isButton = automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, UIA_ControlTypeIds.UIA_ButtonControlTypeId);
            IUIAutomationElementArray buttons = root.FindAll(TreeScope.TreeScope_Subtree, isButton);

            var candidates = new List<CloseButtonCandidate>();
            int count = buttons.Length;
            for (int i = 0; i < count; i++)
            {
                IUIAutomationElement element = buttons.GetElement(i);
                tagRECT rect = element.CurrentBoundingRectangle;
                candidates.Add(new CloseButtonCandidate(
                    element.CurrentAutomationId ?? string.Empty,
                    element.CurrentName ?? string.Empty,
                    element.CurrentLocalizedControlType ?? string.Empty,
                    Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom)));
            }

            CloseButtonCandidate? best = CloseButtonMatcher.FindBestMatch(candidates, windowRect);
            tcs.TrySetResult(best?.BoundingRectangle);
        }
        catch (Exception)
        {
            // Câu hỏi mở chưa validate thực nghiệm (Architecture/07 mục 6): UIPI có thể chặn Low IL
            // đọc cây UIA của cửa sổ Medium/High IL — thất bại ở đây là kỳ vọng được, lớp 3 vẫn an toàn.
            // Bắt rộng có chủ đích: COM interop (tlbimp) có thể ném ExternalException/SEHException hoặc
            // các loại khác ngoài 4 loại đã liệt kê trước đây — để lọt bất kỳ loại nào ra khỏi Thread STA
            // nền này sẽ crash toàn bộ Overlay.exe (exception chưa bắt trên background thread luôn crash
            // process trong .NET), tắt lớp che màn hình ngay lập tức. .NET tự loại OutOfMemoryException/
            // StackOverflowException khỏi `catch (Exception)` nên không cần lo 2 loại đó.
            tcs.TrySetResult(null);
        }
    }
}
