namespace MsAds.Auditor.Ai;

/// <summary>Which AI service writes the commentary.</summary>
public enum AiProvider
{
    Anthropic,
    OpenAi,
}

/// <summary>
/// The optional AI commentary layer.
/// </summary>
/// <remarks>
/// Off unless an API key is supplied. The deterministic analysis is unaffected either way —
/// this only adds a written commentary alongside it, and the application is fully functional
/// and free to run without it.
/// </remarks>
public sealed class AiOptions
{
    public const string SectionName = "Auditor:Ai";

    /// <summary>Which service to use. Both are billed by the provider, not by this application.</summary>
    public AiProvider Provider { get; set; } = AiProvider.Anthropic;

    /// <summary>
    /// The provider's API key. Without it the feature stays off and nothing is ever sent
    /// anywhere.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model to use. Left blank, each provider's sensible default applies — a capable model
    /// for Anthropic, a cheap one for OpenAI. Name one explicitly to trade cost against quality.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Ceiling on the commentary length. A few short paragraphs needs little.</summary>
    public int MaxTokens { get; set; } = 1500;

    /// <summary>
    /// Whether every report gets a commentary without being asked. Off by default, because each
    /// one costs money: the commentary is requested per report with a button instead.
    /// </summary>
    public bool Automatic { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>The model actually used, applying the per-provider default when none is named.</summary>
    public string EffectiveModel => string.IsNullOrWhiteSpace(Model)
        ? Provider switch
        {
            AiProvider.OpenAi => "gpt-4o-mini",
            _ => "claude-opus-5",
        }
        : Model;
}
