using Blazored.Modal;
using Blazored.Modal.Services;
using Microsoft.AspNetCore.Components;
using Muxarr.Data.Entities;
using Muxarr.Web.Components.Shared.Modals;

namespace Muxarr.Web.Helpers;

public static class ModalExtensions
{
    public static async Task<bool> ShowConfirm(
        this IModalService modal,
        string message,
        string title = "Confirm")
    {
        var parameters = new ModalParameters()
            .Add(nameof(ConfirmModal.Message), message);

        var result = modal.Show<ConfirmModal>(title, parameters);
        return (await result.Result).Confirmed;
    }

    public static async Task ShowAlert(
        this IModalService modal,
        string message,
        string title = "Notice")
    {
        var parameters = new ModalParameters()
            .Add(nameof(AlertModal.Message), message);

        var result = modal.Show<AlertModal>(title, parameters);
        await result.Result;
    }

    public static async Task<List<TrackSnapshot>?> ShowCustomConversion(
        this IModalService modal,
        ICollection<MediaTrack> tracks,
        List<MediaTrack> defaultAllowedTracks,
        Profile? profile = null)
    {
        var parameters = new ModalParameters()
            .Add(nameof(CustomConversionModal.Tracks), tracks)
            .Add(nameof(CustomConversionModal.DefaultAllowedTracks), defaultAllowedTracks)
            .Add(nameof(CustomConversionModal.Profile), profile);

        var options = new ModalOptions { Size = ModalSize.Large };
        var result = modal.Show<CustomConversionModal>("Custom Conversion", parameters, options);
        var modalResult = await result.Result;

        if (modalResult.Cancelled)
        {
            return null;
        }

        return (List<TrackSnapshot>?)modalResult.Data;
    }
}
