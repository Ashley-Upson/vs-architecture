// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core2.Models;
internal sealed record ProjectModelPresentation(ProjectModel Model, IReadOnlyDictionary<string, string> Labels);