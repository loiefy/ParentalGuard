namespace ParentalGuard.Vision.Inference;

/// <summary>
/// Bước 5 (Architecture/05-image-pipeline-architecture.md mục 2, ADR-47 — PROPOSED, chờ benchmark
/// theo câu hỏi mở còn lại ở `Specification/09-image-processing-spec.md` mục 8): tổng hợp 5 xác
/// suất lớp thành 1 risk score duy nhất.
/// </summary>
public static class RiskScoreAggregator
{
    /// <summary>risk_score = P(hentai) + P(porn) + P(sexy), clamp [0,1] (đề phòng sai số dấu phẩy động).</summary>
    public static float Aggregate(NsfwClassProbabilities probabilities)
    {
        float sum = probabilities.Hentai + probabilities.Porn + probabilities.Sexy;
        return Math.Clamp(sum, 0f, 1f);
    }
}
