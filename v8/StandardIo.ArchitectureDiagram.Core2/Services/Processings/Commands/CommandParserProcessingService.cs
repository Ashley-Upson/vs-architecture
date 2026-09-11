// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
internal sealed class CommandParserProcessingService(StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands.IRenderConfigurationService configurationService) : ICommandParserProcessingService
{
    public DiagramRenderRequest Parse(string[] command)
    {
        ArgumentNullException.ThrowIfNull(argument: command);

        if (command.Length == 1 && command[0] is "--help" or "-h")
        {
            return new DiagramRenderRequest
            {
                ShowHelp = true
            };
        }

        if (command.Length < 2)
        {
            throw new ArgumentException(DiagramRenderCommand.Usage);
        }

        DiagramTypes diagramType = ParseEnum<DiagramTypes>(value: command[0].Trim().Equals("Data", StringComparison.OrdinalIgnoreCase) ? "DataModel" : command[0]);
        string? output = null;
        DiagramFormats? format = null;
        bool noDuplicates = false;
        bool colourLines = false;
        int maxLayoutIterations = 1000;
        double horizontalOffset = 10;
        string? configPath = null;
        var overrides = new HashSet<string>();
        var projects = new List<string>();
        bool positionalFormat = false;

        for (int index = 1; index < command.Length; index++)
        {
            string argument = command[index];
            overrides.Add(argument);

            if (index == 1 && Enum.TryParse(value: argument, ignoreCase: true, result: out DiagramFormats parsedFormat) &&
                Enum.IsDefined(value: parsedFormat))
            {
                format = parsedFormat;
                positionalFormat = true;
                continue;
            }

            if (argument is "--config" or "-c")
            {
                if (++index >= command.Length || string.IsNullOrWhiteSpace(command[index]) || command[index].StartsWith('-')) throw new ArgumentException("Missing configuration file path.");
                if (configPath is not null) throw new ArgumentException("Supply config only once.");
                configPath = command[index];
                continue;
            }

            if (argument == "--max-layout-iterations")
            {
                if (++index >= command.Length || !int.TryParse(command[index], out maxLayoutIterations) || maxLayoutIterations <= 0)
                    throw new ArgumentException("Maximum layout iterations must be a positive integer.");
                continue;
            }

            if (argument == "--colour-lines")
            {
                colourLines = true;
                continue;
            }

            if (argument == "--horizontal-offset")
            {
                if (++index >= command.Length || !double.TryParse(command[index], NumberStyles.Float, CultureInfo.InvariantCulture, out horizontalOffset) || !double.IsFinite(horizontalOffset) || horizontalOffset <= 0)
                {
                    throw new ArgumentException("Horizontal offset must be a positive finite number.");
                }

                continue;
            }

            if (argument == "--noduplicates")
            {
                noDuplicates = true;
                continue;
            }

            if (argument is "--output" or "-o" or "--format" or "-f")
            {
                if (++index >= command.Length || string.IsNullOrWhiteSpace(value: command[index]) || command[index].StartsWith(value: '-'))
                {
                    throw new ArgumentException("Missing value for " + argument);
                }

                if (argument is "--output" or "-o")
                {
                    if (output is not null)
                    {
                        throw new ArgumentException("Supply output only once.");
                    }

                    output = Path.GetFullPath(path: command[index]);
                }
                else
                {
                    if (format is not null)
                    {
                        throw new ArgumentException("Supply format only once.");
                    }

                    format = ParseEnum<DiagramFormats>(value: command[index]);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(value: argument) || argument.StartsWith(value: '-'))
                {
                    throw new ArgumentException("Unknown argument: " + argument);
                }

                if (!string.Equals(a: Path.GetExtension(path: argument), b: ".csproj", comparisonType: StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Expected a .csproj path: " + argument);
                }

                projects.Add(item: Path.GetFullPath(path: argument));
            }
        }

        if (output is null && positionalFormat)
        {
            string extension = format switch
            {
                DiagramFormats.DrawIO => ".drawio",
                DiagramFormats.Html => ".html",
                DiagramFormats.Json => ".json",
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

            output = Path.GetFullPath(path: diagramType + extension);
        }

        if (output is null || projects.Count == 0)
        {
            throw new ArgumentException(DiagramRenderCommand.Usage);
        }

        var configuration = configurationService.Load(configPath);
        if (overrides.Contains("--noduplicates")) configuration.NoDuplicates = noDuplicates;
        if (overrides.Contains("--colour-lines")) configuration.ColourLines = colourLines;
        if (overrides.Contains("--horizontal-offset")) configuration.HorizontalOffset = horizontalOffset;
        if (overrides.Contains("--max-layout-iterations")) configuration.MaxLayoutIterations = maxLayoutIterations;
        configurationService.Validate(configuration);
        return new DiagramRenderRequest
        {
            ProjectPaths = projects.ToArray(),
            RenderConfiguration = configuration,
            OutputPath = output,
            Format = format ?? DiagramFormats.DrawIO,
            DiagramType = diagramType
        };
    }

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum
    {
        string name = value.Trim();

        if (!Enum.TryParse(value: name, ignoreCase: true, result: out TEnum result) ||
            !Enum.IsDefined(value: result) ||
            !string.Equals(a: result.ToString(), b: name, comparisonType: StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(message: $"Unknown {typeof(TEnum).Name}: {value}", paramName: nameof(value));
        }

        return result;
    }
}
