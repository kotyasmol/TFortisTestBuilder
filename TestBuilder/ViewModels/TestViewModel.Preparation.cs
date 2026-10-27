using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestBuilder.Services;
using TestBuilder.Services.Graph;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    // This state belongs only to the currently opened document, never to a file catalogue.
    private string? _preparedProfileKey;
    private Dictionary<string, object> _profilePreparationVariables = new();
    private bool _isPreparingProfile;
    private bool CanPrepareProfile => !IsNetworkSetupBusy && !IsLabelToolBusy && !NeedsStandInspection && !IsTestRunning && !IsConnecting &&
        App.StartupOptions.IsValid && _profileLoadError.Length == 0;
    private bool HasPendingPreparation => _preparedProfileKey == null && BoardNodes(RootGraph).Any(node => node.RunOnlyOnFirstRun);

    private string CurrentPreparationKey() =>
        FirstRunProfileKey.Create(_currentProfilePath, GraphSerializer.Serialize(this, CurrentProfileName));

    private void ResetProfilePreparation()
    {
        _preparedProfileKey = null;
        _profilePreparationVariables.Clear();
    }

    internal async Task<bool> PrepareOpenedProfileAsync()
    {
        if (IsTestRunning || IsConnecting || NeedsStandInspection || _profileLoadError.Length > 0) return false;
        // Profiles without preparation retain their existing execution and validation path.
        if (!BoardNodes(RootGraph).Any(node => node.RunOnlyOnFirstRun))
        {
            ResetProfilePreparation();
            return true;
        }
        try
        {
            var plan = ProfilePreparationPlan.Create(RootGraph);
            var key = CurrentPreparationKey();
            if (_preparedProfileKey == key) return true;
            ResetProfilePreparation();
            Station.Prepare(RootGraph);
            if (plan.Nodes.Any(GraphConnectionRequirements.RequiresStandConnection) && !IsConnected)
            {
                Station.Headline = "Ожидается подготовка стенда";
                Station.Guidance = "Подключите стенд. Подготовка профиля начнётся автоматически.";
                StatusMessage = Station.Guidance;
                return false;
            }
            _isPreparingProfile = true;
            await ExecuteGraphAsync(plan);
            return _preparedProfileKey == key;
        }
        catch (Exception ex)
        {
            ResetProfilePreparation();
            Station.Prepare(RootGraph);
            Station.CompletePreparation(succeeded: false, NeedsStandInspection);
            Station.Headline = "Не удалось подготовить профиль";
            Station.Guidance = ex.Message;
            StatusMessage = ex.Message;
            TestingLogger.Error($"Подготовка профиля: {ex.Message}");
            return false;
        }
        finally
        {
            _isPreparingProfile = false;
            RefreshStationAvailability();
        }
    }
}
