using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Execution;
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
    private bool ProfileRequiresHardware => RootGraph.Hardware?.Devices.Count > 0 || GraphConnectionRequirements.RequiresStandConnection(RootGraph);
    private bool HasProfilePreparation => BoardNodes(RootGraph).Any(node => node.RunOnlyOnFirstRun);
    private bool HasPendingPreparation => _preparedProfileKey == null && HasProfilePreparation;
    private bool ShouldPrepareForRun => HasPendingPreparation && (IsOperatorView || !HasPartialRun);
    public bool ShowRetryProfilePreparation => !IsActionGraphEditor && HasProfilePreparation;
    public bool CanRetryProfilePreparation => ShowRetryProfilePreparation && CanPrepareProfile &&
        !_isPreparingProfile && !IsStopping && !HasRunningQuickActions && (!ProfileRequiresHardware || IsConnected);

    [RelayCommand(CanExecute = nameof(CanRetryProfilePreparation))]
    private async Task RetryProfilePreparationAsync()
    {
        if (!CanRetryProfilePreparation) return;
        // Keep the open graph and unsaved edits. Recheck equipment only if its
        // previous validation was lost; preparation never starts the device test.
        if (ProfileRequiresHardware && !IsMonitoringActive)
        {
            IsConnecting = true;
            try
            {
                if (_registerMonitor != null) await _registerMonitor.StopAsync();
                DisposeRegisterMonitor();
                _registerState.Clear();
                if (!await EnsureConnectedProfileAsync()) return;
            }
            catch (Exception ex)
            {
                StatusMessage = "Не удалось проверить оборудование. Подробности в журнале.";
                LogUiException("Повторная подготовка профиля", ex, TestErrorCode.Preparation);
                return;
            }
            finally { IsConnecting = false; }
        }
        await WithRunFileLogAsync(() => PrepareProfileAsync());
    }

    private string CurrentPreparationKey() =>
        FirstRunProfileKey.Create(_currentProfilePath, GraphSerializer.Serialize(this, CurrentProfileName));

    private void ResetProfilePreparation()
    {
        _preparedProfileKey = null;
        _profilePreparationVariables.Clear();
    }

    private void InvalidateChangedProfilePreparation()
    {
        if (_preparedProfileKey == null) return;
        // Check at save/run boundaries, not on every UI property notification.
        // The key covers nested graphs and excludes layout-only changes.
        try
        {
            if (_preparedProfileKey == CurrentPreparationKey()) return;
        }
        catch
        {
            ResetProfilePreparation();
            RefreshStationAvailability();
            throw;
        }
        ResetProfilePreparation();
        Station.Prepare(RootGraph);
        RefreshStationAvailability();
        if (HasPendingPreparation)
            StatusMessage = "Профиль изменён. Настройка выполнится при следующем запуске.";
    }

    private async Task<bool> PrepareProfileAsync(bool forTestRun = false)
    {
        if (!CanPrepareProfile || _isPreparingProfile || HasRunningQuickActions) return false;
        if (ProfileRequiresHardware && (!IsConnected || !IsMonitoringActive))
        {
            // Keep the equipment failure visible instead of erasing its cause.
            RefreshStationAvailability();
            return false;
        }
        try
        {
            var plan = ProfilePreparationPlan.Create(RootGraph);
            var key = CurrentPreparationKey();
            ResetProfilePreparation();
            Station.Prepare(RootGraph);
            _isPreparingProfile = true;
            await ExecuteGraphAsync(plan, forTestRun);
            return _preparedProfileKey == key;
        }
        catch (Exception ex)
        {
            ResetProfilePreparation();
            Station.Prepare(RootGraph);
            var code = TestErrorCodes.Resolve(ex, TestErrorCode.Preparation);
            Station.CompletePreparation(succeeded: false, NeedsStandInspection, code);
            Station.Headline = "Не удалось подготовить профиль";
            Station.Guidance = ex.Message;
            StatusMessage = ex.Message;
            TestingLogger.Error($"Подготовка профиля: {ex.Message}", code);
            return false;
        }
        finally
        {
            _isPreparingProfile = false;
            RefreshStationAvailability();
        }
    }
}
