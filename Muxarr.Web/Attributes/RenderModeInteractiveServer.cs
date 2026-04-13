using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Muxarr.Web.Attributes;

public class RenderModeInteractiveServer : RenderModeAttribute
{
    public override IComponentRenderMode Mode => RenderMode.InteractiveServer;
}
