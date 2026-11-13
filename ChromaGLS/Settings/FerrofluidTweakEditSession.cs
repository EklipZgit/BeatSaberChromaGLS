using System;

namespace ChromaGLS.Settings
{
    // Session-only ferrofluid tweak lifecycle: Open snapshots the live [Ignore] values,
    // Preview mutates the live config and applies, Save keeps the applied values, Cancel
    // restores the opening snapshot. Nothing here writes persistence or bakes the floor cache.
    internal sealed class FerrofluidTweakEditSession
    {
        private readonly Config _config;
        private readonly Action<Config> _apply;
        private readonly Config _opening = new();

        internal FerrofluidTweakEditSession(Config config, Action<Config> apply)
        {
            _config = config;
            _apply = apply;
        }

        internal bool IsEditing { get; private set; }

        internal void Open()
        {
            FerrofluidTweakSettings.Copy(_config, _opening);
            IsEditing = true;
        }

        internal void Preview(Action<Config> update)
        {
            if (!IsEditing)
                return;

            update(_config);
            _apply(_config);
        }

        internal void Reset()
        {
            if (!IsEditing)
                return;

            FerrofluidTweakSettings.Copy(new Config(), _config);
            _apply(_config);
        }

        internal void Save()
        {
            IsEditing = false;
        }

        internal void Cancel()
        {
            if (!IsEditing)
                return;

            FerrofluidTweakSettings.Copy(_opening, _config);
            _apply(_config);
            IsEditing = false;
        }
    }
}
