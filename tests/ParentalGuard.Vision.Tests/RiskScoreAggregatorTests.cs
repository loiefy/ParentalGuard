using ParentalGuard.Vision.Inference;

namespace ParentalGuard.Vision.Tests;

public class RiskScoreAggregatorTests
{
    [Fact]
    public void Aggregate_SumsHentaiPornSexy_IgnoresDrawingAndNeutral()
    {
        var probabilities = new NsfwClassProbabilities(Drawing: 0.9f, Hentai: 0.1f, Neutral: 0.0f, Porn: 0.2f, Sexy: 0.3f);

        float result = RiskScoreAggregator.Aggregate(probabilities);

        Assert.Equal(0.6f, result, precision: 5);
    }

    [Fact]
    public void Aggregate_ClampsAboveOne()
    {
        var probabilities = new NsfwClassProbabilities(Drawing: 0f, Hentai: 0.5f, Neutral: 0f, Porn: 0.5f, Sexy: 0.5f);

        float result = RiskScoreAggregator.Aggregate(probabilities);

        Assert.Equal(1.0f, result, precision: 5);
    }

    [Fact]
    public void Aggregate_AllZero_ReturnsZero()
    {
        var probabilities = new NsfwClassProbabilities(Drawing: 1f, Hentai: 0f, Neutral: 0f, Porn: 0f, Sexy: 0f);

        float result = RiskScoreAggregator.Aggregate(probabilities);

        Assert.Equal(0f, result, precision: 5);
    }
}
