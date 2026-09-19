namespace ParentalGuard.Vision.Inference;

/// <summary>5 lớp output của <c>GantMan/nsfw_model</c> (`IMG-014`), đúng thứ tự alphabet của model gốc.</summary>
public readonly record struct NsfwClassProbabilities(float Drawing, float Hentai, float Neutral, float Porn, float Sexy);
