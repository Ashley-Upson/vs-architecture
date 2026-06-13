using System;

namespace StandardIo.ArchitectureDiagram.Vsix;

internal static class Guids
{
    public const string PackageString = "0b6455a2-a7c4-4e64-9d0e-732668ff5ab3";
    public const string CommandSetString = "761f51ce-1ca6-4f22-9f32-4c45e94cb92d";

    public static readonly Guid CommandSet = new(CommandSetString);
}
