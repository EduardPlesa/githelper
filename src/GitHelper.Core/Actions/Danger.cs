namespace GitHelper.Core.Actions;

/// <summary>How much a user should be slowed down before an action runs.</summary>
public enum Danger
{
    /// <summary>Runs immediately; the explanation is shown alongside.</summary>
    Safe,

    /// <summary>Requires an explicit confirmation.</summary>
    Caution,

    /// <summary>Requires confirmation plus a consequence sentence. Never suppressible.</summary>
    Destructive,
}
