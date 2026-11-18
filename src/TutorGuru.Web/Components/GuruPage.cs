using Microsoft.AspNetCore.Components;
using MudBlazor;
using TutorGuru.Web.Services;
namespace TutorGuru.Web.Components;
// Base for protected pages: checks /auth/me and profile on first OnAfterRender (no prerender JS).
public abstract class GuruPage : ComponentBase
{
    [Inject] protected GuruApi Api { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected ISnackbar Snack { get; set; } = default!;
    [Inject] protected IDialogService Dialogs { get; set; } = default!;
    protected bool Ready, Loading = true;
    protected string? LoadError;
    protected virtual bool NeedProfile => true;
    protected bool IsTutor => Api.Role == "tutor";
    protected bool IsParent => Api.Role == "parent";
    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (!first) return;
        try { await Api.Send("/auth/me"); }
        catch (ApiException e) when (e.Status == 401) { Nav.NavigateTo("/sign-in"); return; }
        catch (Exception e) { LoadError = e.Message; Loading = false; StateHasChanged(); return; }
        await Api.RefreshProfile();
        if (NeedProfile && Api.Profile == null) { Nav.NavigateTo("/onboarding"); return; }
        Ready = true;
        await Reload();
    }
    protected async Task Reload()
    {
        Loading = true; LoadError = null; StateHasChanged();
        try { await Load(); } catch (Exception e) { LoadError = e.Message; }
        Loading = false; StateHasChanged();
    }
    protected virtual Task Load() => Task.CompletedTask;
    protected async Task<bool> Run(Func<Task> a, string? ok = null)
    {
        try { await a(); if (ok != null) Snack.Add(ok, Severity.Success); return true; }
        catch (Exception e) { Snack.Add(e.Message, Severity.Error); return false; }
    }
    protected async Task<bool> Confirm(string title, string msg)
        => await Dialogs.ShowMessageBox(title, msg, yesText: "Confirm", cancelText: "Cancel") == true;
}
