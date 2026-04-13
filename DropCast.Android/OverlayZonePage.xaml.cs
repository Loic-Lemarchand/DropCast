using DropCast.Android.Services;

namespace DropCast.Android;

public partial class OverlayZonePage : ContentPage
{
    private double _areaW, _areaH;

    // Zone bounds in pixels (within the preview area)
    private double _zoneX, _zoneY, _zoneW, _zoneH;

    // Pan gesture start state
    private double _panStartX, _panStartY, _panStartW, _panStartH;

    private const double MinZoneFraction = 0.15;
    private const double HandleSize = 44;

    public OverlayZonePage()
    {
        InitializeComponent();
    }

    private void OnAreaSizeChanged(object? sender, EventArgs e)
    {
        _areaW = PreviewArea.Width;
        _areaH = PreviewArea.Height;

        if (_areaW <= 0 || _areaH <= 0) return;

        // Load saved zone (stored as fractions) and convert to pixels
        _zoneX = AppSettings.OverlayZoneLeft * _areaW;
        _zoneY = AppSettings.OverlayZoneTop * _areaH;
        _zoneW = AppSettings.OverlayZoneWidth * _areaW;
        _zoneH = AppSettings.OverlayZoneHeight * _areaH;

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_areaW <= 0 || _areaH <= 0) return;

        PreviewArea.BatchBegin();

        // Dim overlays — darken everything outside the zone
        AbsoluteLayout.SetLayoutBounds(DimTop, new Rect(0, 0, _areaW, _zoneY));
        AbsoluteLayout.SetLayoutBounds(DimBottom, new Rect(0, _zoneY + _zoneH, _areaW, _areaH - _zoneY - _zoneH));
        AbsoluteLayout.SetLayoutBounds(DimLeft, new Rect(0, _zoneY, _zoneX, _zoneH));
        AbsoluteLayout.SetLayoutBounds(DimRight, new Rect(_zoneX + _zoneW, _zoneY, _areaW - _zoneX - _zoneW, _zoneH));

        // Zone border
        AbsoluteLayout.SetLayoutBounds(ZoneBox, new Rect(_zoneX, _zoneY, _zoneW, _zoneH));

        // Rule-of-thirds grid lines
        double thirdW = _zoneW / 3;
        double thirdH = _zoneH / 3;
        AbsoluteLayout.SetLayoutBounds(GridV1, new Rect(_zoneX + thirdW, _zoneY, 1, _zoneH));
        AbsoluteLayout.SetLayoutBounds(GridV2, new Rect(_zoneX + thirdW * 2, _zoneY, 1, _zoneH));
        AbsoluteLayout.SetLayoutBounds(GridH1, new Rect(_zoneX, _zoneY + thirdH, _zoneW, 1));
        AbsoluteLayout.SetLayoutBounds(GridH2, new Rect(_zoneX, _zoneY + thirdH * 2, _zoneW, 1));

        // Corner handles
        double hs = HandleSize / 2;
        AbsoluteLayout.SetLayoutBounds(HandleTL, new Rect(_zoneX - hs, _zoneY - hs, HandleSize, HandleSize));
        AbsoluteLayout.SetLayoutBounds(HandleTR, new Rect(_zoneX + _zoneW - hs, _zoneY - hs, HandleSize, HandleSize));
        AbsoluteLayout.SetLayoutBounds(HandleBL, new Rect(_zoneX - hs, _zoneY + _zoneH - hs, HandleSize, HandleSize));
        AbsoluteLayout.SetLayoutBounds(HandleBR, new Rect(_zoneX + _zoneW - hs, _zoneY + _zoneH - hs, HandleSize, HandleSize));

        PreviewArea.BatchCommit();

        // Size label
        int wPct = (int)Math.Round(_zoneW / _areaW * 100);
        int hPct = (int)Math.Round(_zoneH / _areaH * 100);
        ZoneSizeLabel.Text = $"{wPct}% × {hPct}%";
    }

    // --- Move the entire zone ---
    private void OnZonePan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _zoneX;
                _panStartY = _zoneY;
                break;
            case GestureStatus.Running:
                double dx = e.TotalX + (_zoneX - _panStartX);
                double dy = e.TotalY + (_zoneY - _panStartY);
                _zoneX = Math.Clamp(_panStartX + dx, 0, _areaW - _zoneW);
                _zoneY = Math.Clamp(_panStartY + dy, 0, _areaH - _zoneH);
                UpdateVisuals();
                break;
        }
    }

    // --- Resize from top-left corner ---
    private void OnHandleTLPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _zoneX; _panStartY = _zoneY;
                _panStartW = _zoneW; _panStartH = _zoneH;
                break;
            case GestureStatus.Running:
                double minW = MinZoneFraction * _areaW;
                double minH = MinZoneFraction * _areaH;
                double dx = e.TotalX + (_zoneX - _panStartX);
                double dy = e.TotalY + (_zoneY - _panStartY);
                double newX = Math.Clamp(_panStartX + dx, 0, _panStartX + _panStartW - minW);
                double newY = Math.Clamp(_panStartY + dy, 0, _panStartY + _panStartH - minH);
                _zoneW = _panStartW + (_panStartX - newX);
                _zoneH = _panStartH + (_panStartY - newY);
                _zoneX = newX;
                _zoneY = newY;
                UpdateVisuals();
                break;
        }
    }

    // --- Resize from top-right corner ---
    private void OnHandleTRPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartY = _zoneY;
                _panStartW = _zoneW; _panStartH = _zoneH;
                break;
            case GestureStatus.Running:
                double minW = MinZoneFraction * _areaW;
                double minH = MinZoneFraction * _areaH;
                double dx = e.TotalX + (_zoneW - _panStartW);
                double dy = e.TotalY + (_zoneY - _panStartY);
                _zoneW = Math.Clamp(_panStartW + dx, minW, _areaW - _zoneX);
                double newY = Math.Clamp(_panStartY + dy, 0, _panStartY + _panStartH - minH);
                _zoneH = _panStartH + (_panStartY - newY);
                _zoneY = newY;
                UpdateVisuals();
                break;
        }
    }

    // --- Resize from bottom-left corner ---
    private void OnHandleBLPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _zoneX;
                _panStartW = _zoneW; _panStartH = _zoneH;
                break;
            case GestureStatus.Running:
                double minW = MinZoneFraction * _areaW;
                double minH = MinZoneFraction * _areaH;
                double dx = e.TotalX + (_zoneX - _panStartX);
                double dy = e.TotalY + (_zoneH - _panStartH);
                double newX = Math.Clamp(_panStartX + dx, 0, _panStartX + _panStartW - minW);
                _zoneW = _panStartW + (_panStartX - newX);
                _zoneX = newX;
                _zoneH = Math.Clamp(_panStartH + dy, minH, _areaH - _zoneY);
                UpdateVisuals();
                break;
        }
    }

    // --- Resize from bottom-right corner ---
    private void OnHandleBRPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartW = _zoneW; _panStartH = _zoneH;
                break;
            case GestureStatus.Running:
                double minW = MinZoneFraction * _areaW;
                double minH = MinZoneFraction * _areaH;
                double dx = e.TotalX + (_zoneW - _panStartW);
                double dy = e.TotalY + (_zoneH - _panStartH);
                _zoneW = Math.Clamp(_panStartW + dx, minW, _areaW - _zoneX);
                _zoneH = Math.Clamp(_panStartH + dy, minH, _areaH - _zoneY);
                UpdateVisuals();
                break;
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        AppSettings.OverlayZoneLeft = (float)(_zoneX / _areaW);
        AppSettings.OverlayZoneTop = (float)(_zoneY / _areaH);
        AppSettings.OverlayZoneWidth = (float)(_zoneW / _areaW);
        AppSettings.OverlayZoneHeight = (float)(_zoneH / _areaH);

        await Shell.Current.GoToAsync("..");
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _zoneX = 0;
        _zoneY = 0;
        _zoneW = _areaW;
        _zoneH = _areaH;
        UpdateVisuals();
    }
}