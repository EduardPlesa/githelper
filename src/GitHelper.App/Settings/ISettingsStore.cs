namespace GitHelper.App.Settings;

/// <summary>
/// Persistence for <see cref="AppSettings"/>. Viewmodels depend on this rather than the
/// filesystem, so their tests need no temp directories.
/// </summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
