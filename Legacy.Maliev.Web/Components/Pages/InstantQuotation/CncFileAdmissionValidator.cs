// <copyright file="CncFileAdmissionValidator.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Bounded structural admission checks for CNC model and drawing uploads. These checks reject
    /// renamed or obviously incoherent documents; browser-local CAD decoding remains authoritative
    /// for quotation geometry.
    /// </summary>
    internal static class CncFileAdmissionValidator
    {
        private const int MaximumParsedObjects = 20_000;

        private const int MaximumStepReferenceDepth = 32;

        private const int MaximumStepReferenceVisits = 4_096;

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        private static readonly HashSet<string> StepRepresentations = new HashSet<string>(StringComparer.Ordinal)
        {
            "ADVANCED_BREP_SHAPE_REPRESENTATION", "MANIFOLD_SURFACE_SHAPE_REPRESENTATION",
            "SHAPE_REPRESENTATION", "GEOMETRICALLY_BOUNDED_SURFACE_SHAPE_REPRESENTATION",
        };

        private static readonly HashSet<int> SupportedIgesEntities = new HashSet<int>
        {
            100, 102, 104, 106, 108, 110, 112, 114, 116, 118, 120, 122, 124, 126, 128, 130,
            140, 142, 143, 144, 186, 190, 192, 194, 196, 198, 314, 406, 502, 504, 508, 510, 514,
        };

        private static readonly HashSet<int> IgesGeometryEntities = new HashSet<int>
        {
            128, 143, 144, 186, 190, 192, 194, 196, 198, 510, 514,
        };

        private static readonly HashSet<string> StepSplineCurves = new HashSet<string>(StringComparer.Ordinal)
        {
            "B_SPLINE_CURVE", "B_SPLINE_CURVE_WITH_KNOTS", "RATIONAL_B_SPLINE_CURVE",
            "BEZIER_CURVE", "QUASI_UNIFORM_CURVE", "UNIFORM_CURVE",
        };

        private static readonly HashSet<string> StepSplineSurfaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "B_SPLINE_SURFACE", "B_SPLINE_SURFACE_WITH_KNOTS", "RATIONAL_B_SPLINE_SURFACE",
            "BEZIER_SURFACE", "QUASI_UNIFORM_SURFACE", "UNIFORM_SURFACE",
        };

        private static readonly HashSet<string> StepCurveForms = new HashSet<string>(StringComparer.Ordinal)
        {
            ".POLYLINE_FORM.", ".CIRCULAR_ARC.", ".ELLIPTIC_ARC.", ".PARABOLIC_ARC.",
            ".HYPERBOLIC_ARC.", ".UNSPECIFIED.",
        };

        private static readonly HashSet<string> StepSurfaceForms = new HashSet<string>(StringComparer.Ordinal)
        {
            ".PLANE_SURF.", ".CYLINDRICAL_SURF.", ".CONICAL_SURF.", ".SPHERICAL_SURF.",
            ".TOROIDAL_SURF.", ".SURF_OF_REVOLUTION.", ".RULED_SURF.", ".GENERALISED_CONE.",
            ".QUADRIC_SURF.", ".SURF_OF_LINEAR_EXTRUSION.", ".UNSPECIFIED.",
        };

        private static readonly HashSet<string> StepKnotTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            ".UNIFORM_KNOTS.", ".QUASI_UNIFORM_KNOTS.", ".PIECEWISE_BEZIER_KNOTS.", ".UNSPECIFIED.",
        };

        private static readonly HashSet<string> StepSurfaceCurvePreferences = new HashSet<string>(StringComparer.Ordinal)
        {
            ".CURVE_3D.", ".PCURVE_S1.", ".PCURVE_S2.",
        };

        /// <summary>
        /// Performs bounded STEP Part 21 format admission without claiming to validate CAD
        /// topology. Geometry decoding belongs to the browser-local CAD parser; this check only
        /// rejects renamed binary content and documents without a plausible exchange envelope.
        /// </summary>
        /// <param name="data">The uploaded file bytes.</param>
        /// <returns><see langword="true" /> when the STEP envelope is plausible.</returns>
        internal static bool HasValidStepEnvelope(byte[] data)
        {
            if (data == null || data.Length < 96 || data.Any(value => value == 0))
            {
                return false;
            }

            int offset = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            string input = Encoding.ASCII.GetString(data, offset, data.Length - offset).Trim();
            if (!TryFindStepControlStatement(input, "ISO-10303-21", 0, out int documentStart, out int startEnd)
                || documentStart != 0
                || !TryFindStepControlStatement(input, "HEADER", startEnd, out int headerStart, out int headerStatementEnd)
                || !TryFindStepControlStatement(input, "ENDSEC", headerStatementEnd, out int headerEnd, out int headerStatementClose)
                || !TryFindStepControlStatement(input, "DATA", headerStatementClose, out int dataStart, out int dataStatementEnd)
                || !TryFindStepControlStatement(input, "ENDSEC", dataStatementEnd, out int dataEnd, out int dataStatementClose)
                || !TryFindStepControlStatement(input, "END-ISO-10303-21", dataStatementClose, out int documentEnd, out int documentStatementEnd)
                || documentStatementEnd != input.Length)
            {
                return false;
            }

            string header = input.Substring(headerStart, headerEnd - headerStart);
            if (!header.Contains("FILE_DESCRIPTION", StringComparison.Ordinal)
                || !header.Contains("FILE_NAME", StringComparison.Ordinal)
                || !header.Contains("FILE_SCHEMA", StringComparison.Ordinal))
            {
                return false;
            }

            string trailing = input.Substring(dataStatementClose, documentEnd - dataStatementClose);
            if (!string.IsNullOrWhiteSpace(trailing))
            {
                return false;
            }

            string records = input.Substring(dataStatementEnd, dataEnd - dataStatementEnd);
            return Regex.IsMatch(
                records,
                @"#\s*[1-9]\d*\s*=\s*[A-Z][A-Z0-9_]*\s*\(",
                RegexOptions.CultureInvariant,
                RegexTimeout)
                && Regex.IsMatch(records, @"#\s*[1-9]\d*\s*=\s*PRODUCT\s*\(", RegexOptions.CultureInvariant, RegexTimeout)
                && (records.Contains("SHAPE_REPRESENTATION", StringComparison.Ordinal)
                    || records.Contains("SOLID_BREP", StringComparison.Ordinal)
                    || records.Contains("SURFACE_MODEL", StringComparison.Ordinal)
                    || records.Contains("TESSELLATED", StringComparison.Ordinal));
        }

        private static bool TryFindStepControlStatement(
            string input,
            string keyword,
            int startIndex,
            out int statementStart,
            out int statementEnd)
        {
            statementStart = input.IndexOf(keyword, startIndex, StringComparison.Ordinal);
            statementEnd = -1;
            if (statementStart < 0)
            {
                return false;
            }

            int cursor = statementStart + keyword.Length;
            while (cursor < input.Length && char.IsWhiteSpace(input[cursor]))
            {
                cursor++;
            }

            if (cursor >= input.Length || input[cursor] != ';')
            {
                return false;
            }

            statementEnd = cursor + 1;
            return true;
        }

        internal static bool IsValidStep(byte[] data)
        {
            if (!TryTokenizeStep(Encoding.ASCII.GetString(data), out List<string> records)
                || records.Count < 10
                || !string.Equals(records[0], "ISO-10303-21", StringComparison.Ordinal)
                || !string.Equals(records[1], "HEADER", StringComparison.Ordinal)
                || !string.Equals(records[^1], "END-ISO-10303-21", StringComparison.Ordinal))
            {
                return false;
            }

            int headerEnd = records.FindIndex(2, record => string.Equals(record, "ENDSEC", StringComparison.Ordinal));
            if (headerEnd < 5 || headerEnd + 2 >= records.Count || !string.Equals(records[headerEnd + 1], "DATA", StringComparison.Ordinal))
            {
                return false;
            }

            string[] requiredHeaders = { "FILE_DESCRIPTION", "FILE_NAME", "FILE_SCHEMA" };
            foreach (string required in requiredHeaders)
            {
                if (!records.Skip(2).Take(headerEnd - 2).Any(record => TryParseStepCall(record, out string name, out _) && string.Equals(name, required, StringComparison.Ordinal)))
                {
                    return false;
                }
            }

            int dataEnd = records.FindIndex(headerEnd + 2, record => string.Equals(record, "ENDSEC", StringComparison.Ordinal));
            if (dataEnd <= headerEnd + 2 || dataEnd != records.Count - 2)
            {
                return false;
            }

            var entities = new Dictionary<int, StepEntity>();
            foreach (string record in records.Skip(headerEnd + 2).Take(dataEnd - headerEnd - 2))
            {
                if (!TryParseStepEntity(record, out StepEntity? entity) || entities.Count >= MaximumParsedObjects || !entities.TryAdd(entity.Id, entity))
                {
                    return false;
                }
            }

            if (entities.Count < 5 || !entities.Values.Any(entity => entity.Name == "PRODUCT"))
            {
                return false;
            }

            foreach (StepEntity entity in entities.Values)
            {
                if (entity.References.Any(reference => !entities.ContainsKey(reference)))
                {
                    return false;
                }
            }

            return HasCoherentStepProductShape(entities);
        }

        internal static bool IsValidIges(byte[] data)
        {
            string[] lines = Encoding.ASCII.GetString(data).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            if (lines.Length > 0 && lines[^1].Length == 0)
            {
                lines = lines.Take(lines.Length - 1).ToArray();
            }

            if (lines.Length < 7 || lines.Length > MaximumParsedObjects * 4 || lines.Any(line => line.Length != 80))
            {
                return false;
            }

            const string order = "SGDPT";
            var counts = order.ToDictionary(section => section, _ => 0);
            int lastSection = 0;
            foreach (string line in lines)
            {
                int section = order.IndexOf(line[72], StringComparison.Ordinal);
                if (section < lastSection
                    || !int.TryParse(line.Substring(73, 7).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int sequence)
                    || sequence != counts[line[72]] + 1)
                {
                    return false;
                }

                lastSection = section;
                counts[line[72]]++;
            }

            if (counts.Values.Any(count => count == 0) || counts['D'] % 2 != 0)
            {
                return false;
            }

            string[] directory = lines.Where(line => line[72] == 'D').ToArray();
            var parameters = new Dictionary<int, string>();
            foreach (string line in lines.Where(line => line[72] == 'P'))
            {
                if (!int.TryParse(line.Substring(73, 7).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parameterSequence)
                    || parameterSequence <= 0
                    || !parameters.TryAdd(parameterSequence, line))
                {
                    return false;
                }
            }

            var directoryTypes = new Dictionary<int, int>();
            for (int index = 0; index < directory.Length; index += 2)
            {
                if (!TryIgesField(directory[index], 0, out int directoryType))
                {
                    return false;
                }

                directoryTypes[index + 1] = directoryType;
            }

            var ownedParameterRecords = new HashSet<int>();
            bool hasGeometry = false;
            for (int index = 0; index < directory.Length; index += 2)
            {
                int directorySequence = index + 1;
                if (!TryIgesField(directory[index], 0, out int entityType)
                    || !SupportedIgesEntities.Contains(entityType)
                    || !TryIgesField(directory[index], 8, out int parameterPointer)
                    || parameterPointer <= 0
                    || !TryIgesField(directory[index + 1], 0, out int repeatedType)
                    || repeatedType != entityType
                    || !TryIgesField(directory[index + 1], 24, out int parameterLineCount)
                    || parameterLineCount <= 0)
                {
                    return false;
                }

                var payload = new StringBuilder();
                for (int offset = 0; offset < parameterLineCount; offset++)
                {
                    if (!parameters.TryGetValue(parameterPointer + offset, out string? parameter)
                        || !TryIgesField(parameter, 64, out int owner)
                        || owner != directorySequence
                        || !ownedParameterRecords.Add(parameterPointer + offset))
                    {
                        return false;
                    }

                    payload.Append(parameter.Substring(0, 64));
                }

                if (!IsValidIgesParameterPayload(entityType, payload.ToString(), directoryTypes))
                {
                    return false;
                }

                hasGeometry |= IgesGeometryEntities.Contains(entityType);
            }

            string terminate = lines.Last(line => line[72] == 'T').Substring(0, 72);
            Match totals = Regex.Match(terminate, @"S\s*(\d+)G\s*(\d+)D\s*(\d+)P\s*(\d+)", RegexOptions.CultureInvariant, RegexTimeout);
            return hasGeometry
                && ownedParameterRecords.Count == parameters.Count
                && totals.Success
                && TryMatchIgesTotal(totals.Groups[1].Value, counts['S'])
                && TryMatchIgesTotal(totals.Groups[2].Value, counts['G'])
                && TryMatchIgesTotal(totals.Groups[3].Value, counts['D'])
                && TryMatchIgesTotal(totals.Groups[4].Value, counts['P']);
        }

        internal static bool IsValidPdf(byte[] data) => new PdfValidator(data).Validate();

        private static bool TryTokenizeStep(string input, out List<string> records)
        {
            records = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            bool comment = false;
            for (int index = 0; index < input.Length; index++)
            {
                char value = input[index];
                char next = index + 1 < input.Length ? input[index + 1] : '\0';
                if (comment)
                {
                    if (value == '*' && next == '/')
                    {
                        comment = false;
                        index++;
                        current.Append(' ');
                    }

                    continue;
                }

                if (!quoted && value == '/' && next == '*')
                {
                    comment = true;
                    index++;
                    continue;
                }

                if (value == '\'' && quoted && next == '\'')
                {
                    current.Append("''");
                    index++;
                    continue;
                }

                if (value == '\'')
                {
                    quoted = !quoted;
                    current.Append(value);
                    continue;
                }

                if (!quoted && value == ';')
                {
                    string record = current.ToString().Trim();
                    if (record.Length == 0 || records.Count >= MaximumParsedObjects)
                    {
                        return false;
                    }

                    records.Add(record);
                    current.Clear();
                    continue;
                }

                current.Append(value);
            }

            return !quoted && !comment && string.IsNullOrWhiteSpace(current.ToString());
        }

        private static bool TryParseStepCall(string record, out string name, out string arguments)
        {
            name = null!;
            arguments = null!;
            Match match = Regex.Match(record, @"\A\s*([A-Z][A-Z0-9_]*)\s*(\(.*\))\s*\z", RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
            if (!match.Success || !HasBalancedStepExpression(match.Groups[2].Value))
            {
                return false;
            }

            name = match.Groups[1].Value;
            arguments = match.Groups[2].Value;
            return true;
        }

        private static bool TryParseStepEntity(string record, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out StepEntity? entity)
        {
            entity = null!;
            Match match = Regex.Match(record, @"\A\s*#(\d+)\s*=\s*([A-Z][A-Z0-9_]*)\s*(\(.*\))\s*\z", RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
            if (!match.Success)
            {
                Match complex = Regex.Match(record, @"\A\s*#(\d+)\s*=\s*(\(\s*[A-Z][A-Z0-9_]*\s*\(.*\)\s*\))\s*\z", RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
                if (!complex.Success
                    || !int.TryParse(complex.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int complexId)
                    || complexId <= 0
                    || !HasBalancedStepExpression(complex.Groups[2].Value))
                {
                    return false;
                }

                string complexExpression = RemoveStepStrings(complex.Groups[2].Value);
                if (!TryParseStepReferences(complexExpression, out int[] complexReferences))
                {
                    return false;
                }

                entity = new StepEntity(complexId, "COMPLEX_ENTITY", Array.Empty<string>(), complexReferences);
                return true;
            }

            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || id <= 0
                || !HasBalancedStepExpression(match.Groups[3].Value))
            {
                return false;
            }

            string expression = RemoveStepStrings(match.Groups[3].Value);
            if (!TryParseStepReferences(expression, out int[] references)
                || !TrySplitStepArguments(match.Groups[3].Value, out IReadOnlyList<string> arguments))
            {
                return false;
            }

            entity = new StepEntity(id, match.Groups[2].Value, arguments, references);
            return true;
        }

        private static bool HasBalancedStepExpression(string expression)
        {
            int depth = 0;
            bool quoted = false;
            for (int index = 0; index < expression.Length; index++)
            {
                if (expression[index] == '\'' && quoted && index + 1 < expression.Length && expression[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                if (expression[index] == '\'')
                {
                    quoted = !quoted;
                }
                else if (!quoted && expression[index] == '(')
                {
                    depth++;
                }
                else if (!quoted && expression[index] == ')' && --depth < 0)
                {
                    return false;
                }
            }

            return !quoted && depth == 0;
        }

        private static string RemoveStepStrings(string expression)
        {
            var result = new StringBuilder(expression.Length);
            bool quoted = false;
            for (int index = 0; index < expression.Length; index++)
            {
                if (expression[index] == '\'' && quoted && index + 1 < expression.Length && expression[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                if (expression[index] == '\'')
                {
                    quoted = !quoted;
                    result.Append(' ');
                }
                else
                {
                    result.Append(quoted ? ' ' : expression[index]);
                }
            }

            return result.ToString();
        }

        private static bool TryParseStepReferences(string expression, out int[] references)
        {
            var parsed = new List<int>();
            foreach (Match reference in Regex.Matches(expression, @"#(\d+)", RegexOptions.CultureInvariant, RegexTimeout))
            {
                if (!int.TryParse(reference.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    || value <= 0)
                {
                    references = Array.Empty<int>();
                    return false;
                }

                parsed.Add(value);
            }

            references = parsed.ToArray();
            return true;
        }

        private static bool TrySplitStepArguments(string expression, out IReadOnlyList<string> arguments)
        {
            var values = new List<string>();
            arguments = values;
            if (expression.Length < 2 || expression[0] != '(' || expression[^1] != ')')
            {
                return false;
            }

            int depth = 0;
            int start = 1;
            bool quoted = false;
            for (int index = 1; index < expression.Length - 1; index++)
            {
                char value = expression[index];
                if (value == '\'' && quoted && index + 1 < expression.Length && expression[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                if (value == '\'')
                {
                    quoted = !quoted;
                }
                else if (!quoted && value == '(')
                {
                    depth++;
                }
                else if (!quoted && value == ')' && --depth < 0)
                {
                    return false;
                }
                else if (!quoted && depth == 0 && value == ',')
                {
                    values.Add(expression.Substring(start, index - start).Trim());
                    start = index + 1;
                }
            }

            if (quoted || depth != 0)
            {
                return false;
            }

            string last = expression.Substring(start, expression.Length - 1 - start).Trim();
            if (last.Length > 0 || values.Count > 0)
            {
                values.Add(last);
            }

            return values.All(value => value.Length > 0);
        }

        private static bool HasCoherentStepProductShape(IReadOnlyDictionary<int, StepEntity> entities)
        {
            foreach (StepEntity shapeDefinition in entities.Values.Where(entity => entity.Name == "SHAPE_DEFINITION_REPRESENTATION"))
            {
                if (!TryArgumentReference(shapeDefinition, 0, out int productShapeId)
                    || !TryArgumentReference(shapeDefinition, 1, out int representationId)
                    || !IsEntity(entities, productShapeId, "PRODUCT_DEFINITION_SHAPE")
                    || !TryArgumentReference(entities[productShapeId], 2, out int productDefinitionId)
                    || !IsEntity(entities, productDefinitionId, "PRODUCT_DEFINITION")
                    || !TryArgumentReference(entities[productDefinitionId], 2, out int formationId)
                    || !entities.TryGetValue(formationId, out StepEntity? formation)
                    || !formation.Name.StartsWith("PRODUCT_DEFINITION_FORMATION", StringComparison.Ordinal)
                    || !TryArgumentReference(formation, 2, out int productId)
                    || !IsEntity(entities, productId, "PRODUCT")
                    || !HasCoherentStepRepresentation(entities, representationId))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool HasCoherentStepRepresentation(IReadOnlyDictionary<int, StepEntity> entities, int representationId)
        {
            var candidates = new HashSet<int> { representationId };
            foreach (StepEntity relationship in entities.Values.Where(entity => entity.Name == "SHAPE_REPRESENTATION_RELATIONSHIP"))
            {
                if (relationship.References.Contains(representationId))
                {
                    foreach (int reference in relationship.References.Where(reference => entities.TryGetValue(reference, out StepEntity? related) && StepRepresentations.Contains(related.Name)))
                    {
                        candidates.Add(reference);
                    }
                }
            }

            foreach (int candidateId in candidates)
            {
                if (!entities.TryGetValue(candidateId, out StepEntity? representation)
                    || !StepRepresentations.Contains(representation.Name)
                    || representation.Arguments.Count < 2
                    || !TryArgumentReferenceList(representation.Arguments[1], out int[] items))
                {
                    continue;
                }

                var traversal = new StepReferenceTraversal();
                if (items.Any(item => HasCoherentStepTopology(entities, item, traversal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCoherentStepTopology(
            IReadOnlyDictionary<int, StepEntity> entities,
            int itemId,
            StepReferenceTraversal traversal)
        {
            if (!entities.TryGetValue(itemId, out StepEntity? item))
            {
                return false;
            }

            if (item.Name == "MANIFOLD_SOLID_BREP" || item.Name == "FACETED_BREP" || item.Name == "BREP_WITH_VOIDS")
            {
                if (!TryArgumentReference(item, 1, out int shellId)
                    || !HasCoherentStepShell(entities, shellId, requireClosed: true, traversal))
                {
                    return false;
                }

                return true;
            }

            if (item.Name == "SHELL_BASED_SURFACE_MODEL" && item.Arguments.Count >= 2
                && TryArgumentReferenceList(item.Arguments[1], out int[] shells) && shells.Length > 0)
            {
                return shells.All(shellId => HasCoherentStepShell(entities, shellId, requireClosed: false, traversal));
            }

            return false;
        }

        private static bool HasCoherentStepShell(
            IReadOnlyDictionary<int, StepEntity> entities,
            int shellId,
            bool requireClosed,
            StepReferenceTraversal traversal)
        {
            if (!entities.TryGetValue(shellId, out StepEntity? shell)
                || (requireClosed ? shell.Name != "CLOSED_SHELL" : shell.Name != "CLOSED_SHELL" && shell.Name != "OPEN_SHELL")
                || shell.Arguments.Count < 2
                || !TryArgumentReferenceList(shell.Arguments[1], out int[] faces)
                || faces.Length == 0)
            {
                return false;
            }

            return faces.All(faceId => HasCoherentStepFace(entities, faceId, traversal));
        }

        private static bool HasCoherentStepFace(
            IReadOnlyDictionary<int, StepEntity> entities,
            int faceId,
            StepReferenceTraversal traversal)
        {
            if (!entities.TryGetValue(faceId, out StepEntity? face)
                || face.Name != "ADVANCED_FACE"
                || face.Arguments.Count < 4
                || !TryArgumentReferenceList(face.Arguments[1], out int[] bounds)
                || bounds.Length == 0
                || !TryArgumentReference(face, 2, out int surfaceId)
                || !IsStepBoolean(face.Arguments[3])
                || !HasCoherentStepSurface(entities, surfaceId, traversal))
            {
                return false;
            }

            foreach (int boundId in bounds)
            {
                if (!entities.TryGetValue(boundId, out StepEntity? bound)
                    || (bound.Name != "FACE_BOUND" && bound.Name != "FACE_OUTER_BOUND")
                    || bound.Arguments.Count < 3
                    || !TryArgumentReference(bound, 1, out int loopId)
                    || !IsStepBoolean(bound.Arguments[2])
                    || !HasCoherentStepLoop(entities, loopId, traversal))
                {
                    return false;
                }

            }

            // Open CASCADE AP214 emits a single coherent FACE_BOUND for the outer loop rather
            // than specializing it as FACE_OUTER_BOUND. A non-empty, structurally coherent loop
            // is still mandatory, so empty/name-only face shells remain rejected.
            return true;
        }

        private static bool HasCoherentStepLoop(
            IReadOnlyDictionary<int, StepEntity> entities,
            int loopId,
            StepReferenceTraversal traversal)
        {
            if (!entities.TryGetValue(loopId, out StepEntity? loop)
                || loop.Name != "EDGE_LOOP"
                || loop.Arguments.Count < 2
                || !TryArgumentReferenceList(loop.Arguments[1], out int[] edges)
                || edges.Length == 0)
            {
                return false;
            }

            return edges.All(edgeId =>
                entities.TryGetValue(edgeId, out StepEntity? orientedEdge)
                && orientedEdge.Name == "ORIENTED_EDGE"
                && orientedEdge.Arguments.Count >= 5
                && TryArgumentReference(orientedEdge, 3, out int edgeCurveId)
                && IsStepBoolean(orientedEdge.Arguments[4])
                && HasCoherentStepEdgeCurve(entities, edgeCurveId, traversal));
        }

        private static bool HasCoherentStepEdgeCurve(
            IReadOnlyDictionary<int, StepEntity> entities,
            int edgeCurveId,
            StepReferenceTraversal traversal)
        {
            return entities.TryGetValue(edgeCurveId, out StepEntity? edgeCurve)
                && edgeCurve.Name == "EDGE_CURVE"
                && edgeCurve.Arguments.Count >= 5
                && TryArgumentReference(edgeCurve, 1, out int startVertex)
                && TryArgumentReference(edgeCurve, 2, out int endVertex)
                && TryArgumentReference(edgeCurve, 3, out int curveId)
                && IsStepBoolean(edgeCurve.Arguments[4])
                && HasCoherentStepVertex(entities, startVertex)
                && HasCoherentStepVertex(entities, endVertex)
                && HasCoherentStepCurve(entities, curveId, traversal);
        }

        private static bool HasCoherentStepVertex(IReadOnlyDictionary<int, StepEntity> entities, int vertexId) =>
            entities.TryGetValue(vertexId, out StepEntity? vertex)
            && vertex.Name == "VERTEX_POINT"
            && TryArgumentReference(vertex, 1, out int pointId)
            && HasCoherentStepPoint(entities, pointId);

        private static bool HasCoherentStepCurve(
            IReadOnlyDictionary<int, StepEntity> entities,
            int curveId,
            StepReferenceTraversal traversal)
        {
            if (!traversal.TryEnter(curveId))
            {
                return false;
            }

            try
            {
                if (!entities.TryGetValue(curveId, out StepEntity? curve))
                {
                    return false;
                }

                if (curve.Name == "LINE")
                {
                    return TryArgumentReference(curve, 1, out int pointId)
                        && TryArgumentReference(curve, 2, out int vectorId)
                        && HasCoherentStepPoint(entities, pointId)
                        && HasCoherentStepVector(entities, vectorId);
                }

                if (curve.Name == "CIRCLE" || curve.Name == "ELLIPSE")
                {
                    return TryArgumentReference(curve, 1, out int placementId)
                        && HasCoherentStepPlacement(entities, placementId)
                        && curve.Arguments.Count >= (curve.Name == "CIRCLE" ? 3 : 4)
                        && curve.Arguments.Skip(2).All(IsFiniteStepNumber);
                }

                if (curve.Name == "SURFACE_CURVE")
                {
                    return curve.Arguments.Count == 4
                        && TryArgumentReference(curve, 1, out int curve3dId)
                        && HasCoherentStepCurve(entities, curve3dId, traversal)
                        && TryArgumentReferenceList(curve.Arguments[2], out int[] associatedGeometry)
                        && associatedGeometry.Length > 0
                        && associatedGeometry.All(reference => HasCoherentStepPcurve(entities, reference, traversal))
                        && StepSurfaceCurvePreferences.Contains(curve.Arguments[3]);
                }

                return StepSplineCurves.Contains(curve.Name) && HasCoherentStepSplineCurve(entities, curve);
            }
            finally
            {
                traversal.Exit(curveId);
            }
        }

        private static bool HasCoherentStepPcurve(
            IReadOnlyDictionary<int, StepEntity> entities,
            int pcurveId,
            StepReferenceTraversal traversal)
        {
            if (!traversal.TryEnter(pcurveId))
            {
                return false;
            }

            try
            {
                if (!entities.TryGetValue(pcurveId, out StepEntity? pcurve)
                    || pcurve.Name != "PCURVE"
                    || pcurve.Arguments.Count != 3
                    || !TryArgumentReference(pcurve, 1, out int surfaceId)
                    || !HasCoherentStepSurface(entities, surfaceId, traversal)
                    || !TryArgumentReference(pcurve, 2, out int representationId)
                    || !entities.TryGetValue(representationId, out StepEntity? representation)
                    || representation.Name != "DEFINITIONAL_REPRESENTATION"
                    || representation.Arguments.Count < 2
                    || !TryArgumentReferenceList(representation.Arguments[1], out int[] representationItems)
                    || representationItems.Length == 0)
                {
                    return false;
                }

                return representationItems.All(item => HasCoherentStepPcurveGeometry(entities, item, traversal));
            }
            finally
            {
                traversal.Exit(pcurveId);
            }
        }

        private static bool HasCoherentStepPcurveGeometry(
            IReadOnlyDictionary<int, StepEntity> entities,
            int itemId,
            StepReferenceTraversal traversal)
        {
            if (!traversal.TryEnter(itemId))
            {
                return false;
            }

            try
            {
                if (!entities.TryGetValue(itemId, out StepEntity? item))
                {
                    return false;
                }

                if (item.Name == "LINE")
                {
                    return TryArgumentReference(item, 1, out int pointId)
                        && TryArgumentReference(item, 2, out int vectorId)
                        && HasCoherentStepPoint(entities, pointId, 2)
                        && HasCoherentStepVector(entities, vectorId, 2);
                }

                return StepSplineCurves.Contains(item.Name) && HasCoherentStepSplineCurve(entities, item, 2);
            }
            finally
            {
                traversal.Exit(itemId);
            }
        }

        private static bool HasCoherentStepSurface(
            IReadOnlyDictionary<int, StepEntity> entities,
            int surfaceId,
            StepReferenceTraversal traversal)
        {
            if (!traversal.TryEnter(surfaceId))
            {
                return false;
            }

            try
            {
                if (!entities.TryGetValue(surfaceId, out StepEntity? surface))
                {
                    return false;
                }

                if (surface.Name == "PLANE" || surface.Name == "CYLINDRICAL_SURFACE" || surface.Name == "CONICAL_SURFACE"
                    || surface.Name == "SPHERICAL_SURFACE" || surface.Name == "TOROIDAL_SURFACE")
                {
                    return TryArgumentReference(surface, 1, out int placementId)
                        && HasCoherentStepPlacement(entities, placementId)
                        && surface.Arguments.Count >= RequiredStepSurfaceArgumentCount(surface.Name)
                        && surface.Arguments.Skip(2).All(IsFiniteStepNumber);
                }

                return StepSplineSurfaces.Contains(surface.Name) && HasCoherentStepSplineSurface(entities, surface);
            }
            finally
            {
                traversal.Exit(surfaceId);
            }
        }

        private static int RequiredStepSurfaceArgumentCount(string name) => name switch
        {
            "PLANE" => 2,
            "CYLINDRICAL_SURFACE" or "SPHERICAL_SURFACE" => 3,
            "CONICAL_SURFACE" or "TOROIDAL_SURFACE" => 4,
            _ => int.MaxValue,
        };

        private static bool HasCoherentStepSplineCurve(IReadOnlyDictionary<int, StepEntity> entities, StepEntity curve, int pointDimensions = 3)
        {
            int expectedArity = curve.Name switch
            {
                "B_SPLINE_CURVE_WITH_KNOTS" => 9,
                "RATIONAL_B_SPLINE_CURVE" => 7,
                _ => 6,
            };
            if (curve.Arguments.Count != expectedArity
                || !TryBoundedStepInteger(curve.Arguments[1], 1, 25, out int degree)
                || !TryStepReferenceAggregate(curve.Arguments[2], out int[] controls)
                || controls.Length < degree + 1
                || controls.Length < 2
                || !controls.All(control => HasCoherentStepPoint(entities, control, pointDimensions))
                || !StepCurveForms.Contains(curve.Arguments[3])
                || !IsStepLogical(curve.Arguments[4])
                || !IsStepLogical(curve.Arguments[5]))
            {
                return false;
            }

            if (curve.Name == "B_SPLINE_CURVE_WITH_KNOTS")
            {
                return HasCoherentStepKnots(curve.Arguments[6], curve.Arguments[7], controls.Length, degree)
                    && StepKnotTypes.Contains(curve.Arguments[8]);
            }

            if (curve.Name == "RATIONAL_B_SPLINE_CURVE")
            {
                return TryStepNumberList(curve.Arguments[6], out double[] weights)
                    && weights.Length == controls.Length
                    && weights.All(weight => weight > 0D);
            }

            return curve.Name == "B_SPLINE_CURVE"
                || curve.Name == "BEZIER_CURVE"
                || curve.Name == "QUASI_UNIFORM_CURVE"
                || curve.Name == "UNIFORM_CURVE";
        }

        private static bool HasCoherentStepSplineSurface(IReadOnlyDictionary<int, StepEntity> entities, StepEntity surface)
        {
            int expectedArity = surface.Name switch
            {
                "B_SPLINE_SURFACE_WITH_KNOTS" => 13,
                "RATIONAL_B_SPLINE_SURFACE" => 9,
                _ => 8,
            };
            if (surface.Arguments.Count != expectedArity
                || !TryBoundedStepInteger(surface.Arguments[1], 1, 25, out int uDegree)
                || !TryBoundedStepInteger(surface.Arguments[2], 1, 25, out int vDegree)
                || !TryStepControlNet(surface.Arguments[3], out int[][] controls)
                || controls.Length < uDegree + 1
                || controls.Length < 2
                || controls[0].Length < vDegree + 1
                || controls[0].Length < 2
                || !controls.SelectMany(row => row).All(control => HasCoherentStepPoint(entities, control))
                || !StepSurfaceForms.Contains(surface.Arguments[4])
                || !IsStepLogical(surface.Arguments[5])
                || !IsStepLogical(surface.Arguments[6])
                || !IsStepLogical(surface.Arguments[7]))
            {
                return false;
            }

            int uControlCount = controls.Length;
            int vControlCount = controls[0].Length;
            if (surface.Name == "B_SPLINE_SURFACE_WITH_KNOTS")
            {
                return HasCoherentStepKnots(surface.Arguments[8], surface.Arguments[10], uControlCount, uDegree)
                    && HasCoherentStepKnots(surface.Arguments[9], surface.Arguments[11], vControlCount, vDegree)
                    && StepKnotTypes.Contains(surface.Arguments[12]);
            }

            if (surface.Name == "RATIONAL_B_SPLINE_SURFACE")
            {
                return TryStepNumberMatrix(surface.Arguments[8], out double[][] weights)
                    && weights.Length == uControlCount
                    && weights.All(row => row.Length == vControlCount && row.All(weight => weight > 0D));
            }

            return surface.Name == "B_SPLINE_SURFACE"
                || surface.Name == "BEZIER_SURFACE"
                || surface.Name == "QUASI_UNIFORM_SURFACE"
                || surface.Name == "UNIFORM_SURFACE";
        }

        private static bool TryStepReferenceAggregate(string argument, out int[] references)
        {
            references = Array.Empty<int>();
            if (!TrySplitStepArguments(argument, out IReadOnlyList<string> tokens) || tokens.Count == 0)
            {
                return false;
            }

            var parsed = new List<int>(tokens.Count);
            foreach (string token in tokens)
            {
                Match match = Regex.Match(token, @"\A\s*#(\d+)\s*\z", RegexOptions.CultureInvariant, RegexTimeout);
                if (!match.Success
                    || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int reference)
                    || reference <= 0)
                {
                    return false;
                }

                parsed.Add(reference);
            }

            references = parsed.ToArray();
            return true;
        }

        private static bool TryStepControlNet(string argument, out int[][] controls)
        {
            controls = Array.Empty<int[]>();
            if (!TrySplitStepArguments(argument, out IReadOnlyList<string> rows) || rows.Count == 0)
            {
                return false;
            }

            var parsed = new List<int[]>(rows.Count);
            foreach (string row in rows)
            {
                if (!TryStepReferenceAggregate(row, out int[] points) || points.Length == 0
                    || (parsed.Count > 0 && points.Length != parsed[0].Length))
                {
                    return false;
                }

                parsed.Add(points);
            }

            controls = parsed.ToArray();
            return true;
        }

        private static bool HasCoherentStepKnots(string multiplicityArgument, string knotArgument, int controlCount, int degree)
        {
            if (!TryStepIntegerList(multiplicityArgument, out int[] multiplicities)
                || !TryStepNumberList(knotArgument, out double[] knots)
                || multiplicities.Length == 0
                || multiplicities.Length != knots.Length
                || multiplicities.Any(value => value <= 0)
                || !knots.Zip(knots.Skip(1), (left, right) => left <= right).All(value => value))
            {
                return false;
            }

            long total = multiplicities.Aggregate(0L, (sum, value) => sum + value);
            return total == (long)controlCount + degree + 1L;
        }

        private static bool TryStepIntegerList(string argument, out int[] values)
        {
            values = Array.Empty<int>();
            if (!TrySplitStepArguments(argument, out IReadOnlyList<string> tokens) || tokens.Count == 0)
            {
                return false;
            }

            var parsed = new List<int>(tokens.Count);
            foreach (string token in tokens)
            {
                if (!TryBoundedStepInteger(token, 0, MaximumParsedObjects, out int value))
                {
                    return false;
                }

                parsed.Add(value);
            }

            values = parsed.ToArray();
            return true;
        }

        private static bool TryStepNumberMatrix(string argument, out double[][] values)
        {
            values = Array.Empty<double[]>();
            if (!TrySplitStepArguments(argument, out IReadOnlyList<string> rows) || rows.Count == 0)
            {
                return false;
            }

            var parsed = new List<double[]>(rows.Count);
            foreach (string row in rows)
            {
                if (!TryStepNumberList(row, out double[] numbers) || numbers.Length == 0
                    || (parsed.Count > 0 && numbers.Length != parsed[0].Length))
                {
                    return false;
                }

                parsed.Add(numbers);
            }

            values = parsed.ToArray();
            return true;
        }

        private static bool TryBoundedStepInteger(string token, int minimum, int maximum, out int value) =>
            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= minimum
            && value <= maximum;

        private static bool IsStepLogical(string token) => token == ".T." || token == ".F." || token == ".U.";

        private static bool HasCoherentStepPlacement(IReadOnlyDictionary<int, StepEntity> entities, int placementId)
        {
            if (!entities.TryGetValue(placementId, out StepEntity? placement)
                || placement.Name != "AXIS2_PLACEMENT_3D"
                || placement.Arguments.Count < 4
                || !TryArgumentReference(placement, 1, out int pointId)
                || !TryArgumentReference(placement, 2, out int axisId)
                || !TryArgumentReference(placement, 3, out int referenceId)
                || !HasCoherentStepPoint(entities, pointId)
                || !TryGetStepDirection(entities, axisId, out double[] axis)
                || !TryGetStepDirection(entities, referenceId, out double[] reference))
            {
                return false;
            }

            double crossX = (axis[1] * reference[2]) - (axis[2] * reference[1]);
            double crossY = (axis[2] * reference[0]) - (axis[0] * reference[2]);
            double crossZ = (axis[0] * reference[1]) - (axis[1] * reference[0]);
            return Math.Abs(crossX) > double.Epsilon || Math.Abs(crossY) > double.Epsilon || Math.Abs(crossZ) > double.Epsilon;
        }

        private static bool HasCoherentStepPoint(IReadOnlyDictionary<int, StepEntity> entities, int pointId) =>
            HasCoherentStepPoint(entities, pointId, 3);

        private static bool HasCoherentStepPoint(IReadOnlyDictionary<int, StepEntity> entities, int pointId, int dimensions) =>
            entities.TryGetValue(pointId, out StepEntity? point)
            && point.Name == "CARTESIAN_POINT"
            && point.Arguments.Count >= 2
            && TryStepNumberList(point.Arguments[1], out double[] values)
            && values.Length == dimensions;

        private static bool HasCoherentStepDirection(IReadOnlyDictionary<int, StepEntity> entities, int directionId) =>
            TryGetStepDirection(entities, directionId, out _);

        private static bool TryGetStepDirection(IReadOnlyDictionary<int, StepEntity> entities, int directionId, out double[] values)
        {
            values = Array.Empty<double>();
            return entities.TryGetValue(directionId, out StepEntity? direction)
                && direction.Name == "DIRECTION"
                && direction.Arguments.Count >= 2
                && TryStepNumberList(direction.Arguments[1], out values)
                && values.Length == 3
                && values.Any(value => Math.Abs(value) > double.Epsilon);
        }

        private static bool HasCoherentStepVector(IReadOnlyDictionary<int, StepEntity> entities, int vectorId) =>
            HasCoherentStepVector(entities, vectorId, 3);

        private static bool HasCoherentStepVector(IReadOnlyDictionary<int, StepEntity> entities, int vectorId, int dimensions) =>
            entities.TryGetValue(vectorId, out StepEntity? vector)
            && vector.Name == "VECTOR"
            && vector.Arguments.Count >= 3
            && TryArgumentReference(vector, 1, out int directionId)
            && TryGetStepDirection(entities, directionId, dimensions, out _)
            && double.TryParse(vector.Arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double magnitude)
            && double.IsFinite(magnitude)
            && magnitude > 0D;

        private static bool TryGetStepDirection(
            IReadOnlyDictionary<int, StepEntity> entities,
            int directionId,
            int dimensions,
            out double[] values)
        {
            values = Array.Empty<double>();
            return entities.TryGetValue(directionId, out StepEntity? direction)
                && direction.Name == "DIRECTION"
                && direction.Arguments.Count >= 2
                && TryStepNumberList(direction.Arguments[1], out values)
                && values.Length == dimensions
                && values.Any(value => Math.Abs(value) > double.Epsilon);
        }

        private static bool TryStepNumberList(string argument, out double[] values)
        {
            values = Array.Empty<double>();
            if (!TrySplitStepArguments(argument, out IReadOnlyList<string> tokens) || tokens.Count == 0)
            {
                return false;
            }

            var parsed = new List<double>(tokens.Count);
            foreach (string token in tokens)
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                {
                    return false;
                }

                parsed.Add(value);
            }

            values = parsed.ToArray();
            return true;
        }

        private static bool IsFiniteStepNumber(string token) =>
            double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value);

        private static bool IsStepBoolean(string token) => token == ".T." || token == ".F.";

        private static bool TryArgumentReference(StepEntity entity, int position, out int reference)
        {
            reference = 0;
            return entity.Arguments.Count > position
                && Regex.Match(entity.Arguments[position], @"\A\s*#(\d+)\s*\z", RegexOptions.CultureInvariant, RegexTimeout) is Match match
                && match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out reference)
                && reference > 0;
        }

        private static bool TryArgumentReferenceList(string argument, out int[] references)
        {
            references = Array.Empty<int>();
            if (!TryParseStepReferences(RemoveStepStrings(argument), out int[] parsed))
            {
                return false;
            }

            references = parsed;
            return true;
        }

        private static bool IsEntity(IReadOnlyDictionary<int, StepEntity> entities, int id, string name) =>
            entities.TryGetValue(id, out StepEntity? entity) && entity.Name == name;

        private static bool TryIgesField(string line, int start, out int value) =>
            int.TryParse(line.Substring(start, 8).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool IsValidIgesParameterPayload(int entityType, string payload, IReadOnlyDictionary<int, int> directoryTypes)
        {
            string trimmed = payload.Trim();
            if (!trimmed.EndsWith(";", StringComparison.Ordinal))
            {
                return false;
            }

            int cursor = 0;
            if (!TryReadIgesNumber(trimmed, ref cursor, out string first)
                || !int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out int declaredType)
                || declaredType != entityType)
            {
                return false;
            }

            var tokens = new List<string> { first };
            while (cursor < trimmed.Length)
            {
                char delimiter = trimmed[cursor++];
                if (delimiter == ';')
                {
                    return string.IsNullOrWhiteSpace(trimmed.Substring(cursor))
                        && HasValidIgesEntityParameters(entityType, tokens, directoryTypes);
                }

                if (delimiter != ',')
                {
                    return false;
                }

                while (cursor < trimmed.Length && char.IsWhiteSpace(trimmed[cursor]))
                {
                    cursor++;
                }

                int tokenStart = cursor;
                int digitStart = cursor;
                while (cursor < trimmed.Length && char.IsDigit(trimmed[cursor]))
                {
                    cursor++;
                }

                if (cursor > digitStart && cursor < trimmed.Length && (trimmed[cursor] == 'H' || trimmed[cursor] == 'h'))
                {
                    if (!int.TryParse(trimmed.Substring(digitStart, cursor - digitStart), NumberStyles.None, CultureInfo.InvariantCulture, out int length))
                    {
                        return false;
                    }

                    cursor++;
                    if (length < 0 || length > trimmed.Length - cursor)
                    {
                        return false;
                    }

                    cursor += length;
                    tokens.Add(trimmed.Substring(tokenStart, cursor - tokenStart));
                }
                else
                {
                    cursor = tokenStart;
                    while (cursor < trimmed.Length && trimmed[cursor] != ',' && trimmed[cursor] != ';')
                    {
                        cursor++;
                    }

                    string token = trimmed.Substring(tokenStart, cursor - tokenStart).Trim();
                    if (token.Length > 0 && token != "$" && !Regex.IsMatch(token, @"\A[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[DEde][+-]?\d+)?\z", RegexOptions.CultureInvariant, RegexTimeout))
                    {
                        return false;
                    }

                    tokens.Add(token);
                }
            }

            return false;
        }

        private static bool HasValidIgesEntityParameters(int entityType, IReadOnlyList<string> tokens, IReadOnlyDictionary<int, int> directoryTypes)
        {
            if (tokens.Count < 2)
            {
                return false;
            }

            bool NumbersFrom(int start, int minimum) => tokens.Count >= minimum && tokens.Skip(start).All(IsFiniteIgesNumberOrOmitted);
            bool ReferenceAt(int index, params int[] expectedTypes) =>
                tokens.Count > index
                && int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pointer)
                && pointer > 0
                && directoryTypes.TryGetValue(pointer, out int referencedType)
                && (expectedTypes.Length == 0 || expectedTypes.Contains(referencedType));

            return entityType switch
            {
                128 => HasValidIges128Parameters(tokens),
                143 => tokens.Count >= 5
                    && IsIgesInteger(tokens[1], 0, 1)
                    && ReferenceAt(2, 128, 190, 192, 194, 196, 198)
                    && TryBoundedIgesCount(tokens[3], tokens.Count - 4, out int boundaryCount)
                    && boundaryCount > 0
                    && Enumerable.Range(0, boundaryCount).All(index => ReferenceAt(4 + index, 141, 142)),
                144 => tokens.Count >= 5
                    && ReferenceAt(1, 128, 143, 190, 192, 194, 196, 198)
                    && IsIgesInteger(tokens[2], 0, 1)
                    && TryBoundedIgesCount(tokens[3], tokens.Count - 5, out int innerBoundaryCount)
                    && (tokens[4] == "0" || ReferenceAt(4, 141, 142))
                    && Enumerable.Range(0, innerBoundaryCount).All(index => ReferenceAt(5 + index, 141, 142)),
                186 => tokens.Count >= 4 && ReferenceAt(1, 514) && IsIgesInteger(tokens[2], 0, 1) && NumbersFrom(3, 4),
                190 => tokens.Count >= 4 && ReferenceAt(1, 116) && ReferenceAt(2, 124) && (tokens[3] == "0" || ReferenceAt(3, 124)),
                192 => tokens.Count >= 4 && ReferenceAt(1, 116) && ReferenceAt(2, 124) && IsFiniteRequiredIgesNumber(tokens[3]),
                194 => tokens.Count >= 5 && ReferenceAt(1, 116) && ReferenceAt(2, 124) && IsFiniteRequiredIgesNumber(tokens[3]) && IsFiniteRequiredIgesNumber(tokens[4]),
                196 => tokens.Count >= 3 && ReferenceAt(1, 116) && IsFiniteRequiredIgesNumber(tokens[2]),
                198 => tokens.Count >= 5 && ReferenceAt(1, 116) && ReferenceAt(2, 124) && IsFiniteRequiredIgesNumber(tokens[3]) && IsFiniteRequiredIgesNumber(tokens[4]),
                510 => tokens.Count >= 5
                    && ReferenceAt(1, 128, 143, 144, 190, 192, 194, 196, 198)
                    && TryBoundedIgesCount(tokens[2], tokens.Count - 4, out int loopCount)
                    && IsIgesInteger(tokens[3], 0, 1)
                    && Enumerable.Range(0, loopCount).All(index => ReferenceAt(4 + index, 508)),
                514 => int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int faceCount)
                    && faceCount > 0
                    && faceCount <= MaximumParsedObjects
                    && tokens.Count >= 2
                    && faceCount <= (tokens.Count - 2) / 2
                    && Enumerable.Range(0, faceCount).All(index => ReferenceAt(2 + (index * 2), 510))
                    && Enumerable.Range(0, faceCount).All(index => IsFiniteIgesNumberOrOmitted(tokens[3 + (index * 2)])),
                _ => tokens.Count >= 2,
            };
        }

        private static bool HasValidIges128Parameters(IReadOnlyList<string> tokens)
        {
            if (tokens.Count < 14
                || !TryBoundedIgesCount(tokens[1], MaximumParsedObjects, out int upperU)
                || !TryBoundedIgesCount(tokens[2], MaximumParsedObjects, out int upperV)
                || !TryBoundedIgesCount(tokens[3], 25, out int degreeU)
                || !TryBoundedIgesCount(tokens[4], 25, out int degreeV)
                || degreeU <= 0
                || degreeV <= 0
                || tokens.Skip(5).Take(5).Any(token => !IsIgesInteger(token, 0, 1)))
            {
                return false;
            }

            long controlPointCount = ((long)upperU + 1L) * ((long)upperV + 1L);
            long required = 14L + upperU + degreeU + 2L + upperV + degreeV + 2L + (4L * controlPointCount);
            return required <= MaximumParsedObjects * 8L
                && tokens.Count >= required
                && tokens.Skip(10).All(IsFiniteRequiredIgesNumber);
        }

        private static bool TryBoundedIgesCount(string token, int maximum, out int value) =>
            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && value >= 0
            && value <= maximum;

        private static bool IsIgesInteger(string token, int minimum, int maximum) =>
            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value >= minimum
            && value <= maximum;

        private static bool IsFiniteRequiredIgesNumber(string token) => token.Length > 0 && token != "$" && IsFiniteIgesNumberOrOmitted(token);

        private static bool IsFiniteIgesNumberOrOmitted(string token)
        {
            if (token == "$" || token.Length == 0)
            {
                return true;
            }

            return double.TryParse(token.Replace('D', 'E').Replace('d', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && !double.IsNaN(value)
                && !double.IsInfinity(value);
        }

        private static bool TryMatchIgesTotal(string text, int expected) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int actual) && actual == expected;

        private static bool TryReadIgesNumber(string text, ref int cursor, out string value)
        {
            int start = cursor;
            while (cursor < text.Length && text[cursor] != ',' && text[cursor] != ';')
            {
                cursor++;
            }

            value = text.Substring(start, cursor - start).Trim();
            return value.Length > 0 && cursor < text.Length;
        }

        private sealed record StepEntity(int Id, string Name, IReadOnlyList<string> Arguments, IReadOnlyCollection<int> References);

        private sealed class StepReferenceTraversal
        {
            private readonly HashSet<int> active = new HashSet<int>();

            private int visits;

            internal bool TryEnter(int entityId)
            {
                if (this.visits >= MaximumStepReferenceVisits
                    || this.active.Count >= MaximumStepReferenceDepth
                    || !this.active.Add(entityId))
                {
                    return false;
                }

                this.visits++;
                return true;
            }

            internal void Exit(int entityId) => this.active.Remove(entityId);
        }

        private sealed class PdfValidator
        {
            private readonly byte[] data;
            private readonly string text;
            private readonly Dictionary<int, XrefEntry> xref = new Dictionary<int, XrefEntry>();

            private string trailerDictionary = string.Empty;

            internal PdfValidator(byte[] data)
            {
                this.data = data;
                this.text = Encoding.Latin1.GetString(data);
            }

            internal bool Validate()
            {
                if (this.data.Length < 32
                    || !Regex.IsMatch(this.text, @"\A%PDF-(?:1\.[0-7]|2\.0)(?:\r?\n|\r)", RegexOptions.CultureInvariant, RegexTimeout)
                    || !this.text.TrimEnd(' ', '\t', '\r', '\n', '\0').EndsWith("%%EOF", StringComparison.Ordinal)
                    || !TryFindStartXref(this.text, out int startXref)
                    || startXref < 0
                    || startXref >= this.data.Length)
                {
                    return false;
                }

                if (StartsWith(this.text, startXref, "xref"))
                {
                    if (!this.ParseClassicXref(startXref))
                    {
                        return false;
                    }
                }
                else if (!this.ParseXrefStream(startXref))
                {
                    return false;
                }

                if (this.xref.Count == 0 || this.xref.Count > MaximumParsedObjects || this.xref.Values.Where(entry => entry.Type == 1).Any(entry => !this.ValidateObjectOffset(entry)))
                {
                    return false;
                }

                if (!TryDictionaryReference(this.trailerDictionary, "Root", out int root)
                    || !this.TryResolveObject(root, out string catalog)
                    || !HasPdfType(catalog, "Catalog")
                    || !TryDictionaryReference(catalog, "Pages", out int pages))
                {
                    return false;
                }

                return this.HasPage(pages, new HashSet<int>(), 0);
            }

            private bool ParseClassicXref(int start)
            {
                int cursor = start + 4;
                int inUse = 0;
                while (true)
                {
                    SkipPdfWhitespace(this.text, ref cursor);
                    if (StartsWith(this.text, cursor, "trailer"))
                    {
                        cursor += 7;
                        SkipPdfWhitespace(this.text, ref cursor);
                        return inUse > 0 && TryExtractDictionary(this.text, cursor, out this.trailerDictionary, out _);
                    }

                    if (!TryReadPdfInteger(this.text, ref cursor, out int first)
                        || !TryReadPdfInteger(this.text, ref cursor, out int count)
                        || first < 0
                        || first > MaximumParsedObjects
                        || count <= 0
                        || count > MaximumParsedObjects - first)
                    {
                        return false;
                    }

                    for (int index = 0; index < count; index++)
                    {
                        if (!TryReadPdfInteger(this.text, ref cursor, out int offset)
                            || !TryReadPdfInteger(this.text, ref cursor, out int generation)
                            || !TryReadPdfToken(this.text, ref cursor, out string status))
                        {
                            return false;
                        }

                        if (status == "n")
                        {
                            this.xref[first + index] = new XrefEntry(1, offset, generation, first + index);
                            inUse++;
                        }
                        else if (status != "f")
                        {
                            return false;
                        }
                    }
                }
            }

            private bool ParseXrefStream(int offset)
            {
                if (!TryParseIndirectAt(this.text, offset, out int objectNumber, out int generation, out string body, out string dictionary, out byte[] stream)
                    || !HasPdfType(dictionary, "XRef")
                    || !TryDictionaryIntArray(dictionary, "W", out int[] widths)
                    || widths.Length != 3
                    || widths.Any(width => width < 0 || width > 8)
                    || !TryDictionaryInteger(dictionary, "Size", out int size)
                    || size <= 0
                    || size > MaximumParsedObjects
                    || !TryDecodePdfStream(dictionary, stream, out byte[] decoded))
                {
                    return false;
                }

                int[] index = TryDictionaryIntArray(dictionary, "Index", out int[] parsedIndex) ? parsedIndex : new[] { 0, size };
                if (index.Length == 0 || index.Length % 2 != 0)
                {
                    return false;
                }

                int cursor = 0;
                for (int pair = 0; pair < index.Length; pair += 2)
                {
                    int first = index[pair];
                    int count = index[pair + 1];
                    if (first < 0 || first > size || count < 0 || count > size - first)
                    {
                        return false;
                    }

                    for (int item = 0; item < count; item++)
                    {
                        if (!TryReadBigEndian(decoded, ref cursor, widths[0], out long type)
                            || !TryReadBigEndian(decoded, ref cursor, widths[1], out long field2)
                            || !TryReadBigEndian(decoded, ref cursor, widths[2], out long field3))
                        {
                            return false;
                        }

                        int entryType = widths[0] == 0 ? 1 : (int)type;
                        if ((entryType == 1 || entryType == 2) && field2 <= int.MaxValue && field3 <= int.MaxValue)
                        {
                            this.xref[first + item] = new XrefEntry(entryType, (int)field2, (int)field3, first + item);
                        }
                    }
                }

                this.trailerDictionary = dictionary;
                return this.xref.ContainsKey(objectNumber) || (objectNumber >= 0 && generation >= 0 && body != null);
            }

            private bool ValidateObjectOffset(XrefEntry entry) =>
                entry.Field2 >= 0
                && entry.Field2 < this.data.Length
                && TryParseIndirectAt(this.text, entry.Field2, out int number, out int generation, out _, out _, out _)
                && number == entry.ObjectNumber
                && generation == entry.Field3;

            private bool TryResolveObject(int objectNumber, out string body)
            {
                body = null!;
                if (!this.xref.TryGetValue(objectNumber, out XrefEntry? entry))
                {
                    return false;
                }

                if (entry.Type == 1)
                {
                    return TryParseIndirectAt(this.text, entry.Field2, out int parsed, out _, out body, out _, out _) && parsed == objectNumber;
                }

                if (entry.Type != 2 || !this.xref.TryGetValue(entry.Field2, out XrefEntry? objectStreamEntry) || objectStreamEntry.Type != 1
                    || !TryParseIndirectAt(this.text, objectStreamEntry.Field2, out _, out _, out _, out string dictionary, out byte[] stream)
                    || !HasPdfType(dictionary, "ObjStm")
                    || !TryDictionaryInteger(dictionary, "N", out int count)
                    || !TryDictionaryInteger(dictionary, "First", out int first)
                    || count <= 0 || count > MaximumParsedObjects
                    || !TryDecodePdfStream(dictionary, stream, out byte[] decoded)
                    || first < 0 || first > decoded.Length)
                {
                    return false;
                }

                string decodedText = Encoding.Latin1.GetString(decoded);
                int cursor = 0;
                var headers = new List<(int Number, int Offset)>();
                for (int index = 0; index < count; index++)
                {
                    if (!TryReadPdfInteger(decodedText, ref cursor, out int number) || !TryReadPdfInteger(decodedText, ref cursor, out int relativeOffset))
                    {
                        return false;
                    }

                    headers.Add((number, relativeOffset));
                }

                int selected = headers.FindIndex(header => header.Number == objectNumber);
                if (selected < 0 || selected != entry.Field3)
                {
                    return false;
                }

                int selectedOffset = headers[selected].Offset;
                int nextOffset = selected + 1 < headers.Count ? headers[selected + 1].Offset : decoded.Length - first;
                if (selectedOffset < 0 || selectedOffset > decoded.Length - first
                    || nextOffset < selectedOffset || nextOffset > decoded.Length - first)
                {
                    return false;
                }

                int start = first + selectedOffset;
                int end = first + nextOffset;

                body = decodedText.Substring(start, end - start).Trim();
                return body.Length > 0;
            }

            private bool HasPage(int objectNumber, HashSet<int> visited, int depth)
            {
                if (depth > 32 || !visited.Add(objectNumber) || !this.TryResolveObject(objectNumber, out string body))
                {
                    return false;
                }

                if (HasPdfType(body, "Page"))
                {
                    return true;
                }

                if (!HasPdfType(body, "Pages") || !TryDictionaryReferences(body, "Kids", out int[] kids) || kids.Length == 0)
                {
                    return false;
                }

                return kids.Any(kid => this.HasPage(kid, visited, depth + 1));
            }

            private static bool TryFindStartXref(string text, out int offset)
            {
                offset = -1;
                Match match = Regex.Match(text, @"startxref\s+(\d+)\s+%%EOF\s*\z", RegexOptions.CultureInvariant, RegexTimeout);
                return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out offset);
            }

            private static bool TryParseIndirectAt(string text, int offset, out int number, out int generation, out string body, out string dictionary, out byte[] stream)
            {
                number = generation = -1;
                body = dictionary = null!;
                stream = null!;
                int cursor = offset;
                SkipPdfWhitespace(text, ref cursor);
                if (!TryReadPdfInteger(text, ref cursor, out number)
                    || !TryReadPdfInteger(text, ref cursor, out generation)
                    || !TryReadPdfToken(text, ref cursor, out string obj)
                    || obj != "obj")
                {
                    return false;
                }

                int bodyStart = cursor;
                int endObject = text.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
                if (endObject < 0)
                {
                    return false;
                }

                body = text.Substring(bodyStart, endObject - bodyStart).Trim();
                int dictionaryStart = text.IndexOf("<<", bodyStart, endObject - bodyStart, StringComparison.Ordinal);
                int afterDictionary = dictionaryStart;
                if (dictionaryStart >= 0 && !TryExtractDictionary(text, dictionaryStart, out dictionary, out afterDictionary))
                {
                    return false;
                }

                if (dictionary != null)
                {
                    int streamMarker = text.IndexOf("stream", afterDictionary, endObject - afterDictionary, StringComparison.Ordinal);
                    if (streamMarker >= 0)
                    {
                        if (!TryDictionaryInteger(dictionary, "Length", out int length) || length < 0)
                        {
                            return false;
                        }

                        int streamStart = streamMarker + 6;
                        if (streamStart < text.Length && text[streamStart] == '\r') streamStart++;
                        if (streamStart < text.Length && text[streamStart] == '\n') streamStart++;
                        if (streamStart > text.Length || length > text.Length - streamStart)
                        {
                            return false;
                        }

                        stream = Encoding.Latin1.GetBytes(text.Substring(streamStart, length));
                    }
                }

                return true;
            }

            private static bool TryDecodePdfStream(string dictionary, byte[] stream, out byte[] decoded)
            {
                decoded = null!;
                if (stream == null)
                {
                    return false;
                }

                if (!Regex.IsMatch(dictionary, @"/Filter\b", RegexOptions.CultureInvariant, RegexTimeout))
                {
                    decoded = stream;
                    return true;
                }

                if (!Regex.IsMatch(dictionary, @"/Filter\s*/FlateDecode\b", RegexOptions.CultureInvariant, RegexTimeout))
                {
                    return false;
                }

                try
                {
                    using var input = new MemoryStream(stream);
                    using var inflater = new ZLibStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > 8 * 1024 * 1024)
                        {
                            return false;
                        }

                        output.Write(buffer, 0, read);
                    }

                    decoded = output.ToArray();
                    return true;
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            }

            private static bool TryExtractDictionary(string text, int start, out string dictionary, out int after)
            {
                dictionary = null!;
                after = start;
                if (!StartsWith(text, start, "<<"))
                {
                    return false;
                }

                int depth = 0;
                bool literal = false;
                int literalDepth = 0;
                for (int index = start; index + 1 < text.Length; index++)
                {
                    if (literal)
                    {
                        if (text[index] == '\\')
                        {
                            index++;
                        }
                        else if (text[index] == '(')
                        {
                            literalDepth++;
                        }
                        else if (text[index] == ')' && --literalDepth == 0)
                        {
                            literal = false;
                        }

                        continue;
                    }

                    if (text[index] == '(')
                    {
                        literal = true;
                        literalDepth = 1;
                    }
                    else if (text[index] == '<' && text[index + 1] == '<')
                    {
                        depth++;
                        index++;
                    }
                    else if (text[index] == '>' && text[index + 1] == '>' && --depth == 0)
                    {
                        after = index + 2;
                        dictionary = text.Substring(start, after - start);
                        return true;
                    }
                }

                return false;
            }

            private static bool TryDictionaryReference(string dictionary, string name, out int objectNumber)
            {
                objectNumber = -1;
                Match match = Regex.Match(dictionary ?? string.Empty, @"/" + Regex.Escape(name) + @"\s+(\d+)\s+\d+\s+R\b", RegexOptions.CultureInvariant, RegexTimeout);
                return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out objectNumber);
            }

            private static bool TryDictionaryReferences(string dictionary, string name, out int[] references)
            {
                references = Array.Empty<int>();
                Match match = Regex.Match(dictionary ?? string.Empty, @"/" + Regex.Escape(name) + @"\s*\[(.*?)\]", RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
                if (!match.Success)
                {
                    return false;
                }

                var parsed = new List<int>();
                foreach (Match item in Regex.Matches(match.Groups[1].Value, @"(\d+)\s+\d+\s+R\b", RegexOptions.CultureInvariant, RegexTimeout))
                {
                    if (!int.TryParse(item.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int reference))
                    {
                        return false;
                    }

                    parsed.Add(reference);
                }

                references = parsed.ToArray();
                return true;
            }

            private static bool TryDictionaryInteger(string dictionary, string name, out int value)
            {
                value = -1;
                Match match = Regex.Match(dictionary ?? string.Empty, @"/" + Regex.Escape(name) + @"\s+(\d+)\b", RegexOptions.CultureInvariant, RegexTimeout);
                return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
            }

            private static bool TryDictionaryIntArray(string dictionary, string name, out int[] values)
            {
                values = Array.Empty<int>();
                Match match = Regex.Match(dictionary ?? string.Empty, @"/" + Regex.Escape(name) + @"\s*\[([^\]]*)\]", RegexOptions.CultureInvariant, RegexTimeout);
                if (!match.Success)
                {
                    return false;
                }

                var parsed = new List<int>();
                foreach (Match item in Regex.Matches(match.Groups[1].Value, @"\d+", RegexOptions.CultureInvariant, RegexTimeout))
                {
                    if (!int.TryParse(item.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                    {
                        return false;
                    }

                    parsed.Add(value);
                }

                values = parsed.ToArray();
                return true;
            }

            private static bool HasPdfType(string dictionary, string type) =>
                Regex.IsMatch(dictionary ?? string.Empty, @"/Type\s*/" + Regex.Escape(type) + @"\b", RegexOptions.CultureInvariant, RegexTimeout);

            private static bool TryReadBigEndian(byte[] bytes, ref int cursor, int width, out long value)
            {
                value = 0;
                if (width < 0 || cursor < 0 || cursor > bytes.Length || width > bytes.Length - cursor)
                {
                    return false;
                }

                for (int index = 0; index < width; index++)
                {
                    value = (value << 8) | bytes[cursor++];
                }

                return true;
            }

            private sealed record XrefEntry(int Type, int Field2, int Field3, int ObjectNumber);
        }

        private static bool StartsWith(string text, int index, string value) =>
            index >= 0 && index + value.Length <= text.Length && string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

        private static void SkipPdfWhitespace(string text, ref int cursor)
        {
            while (cursor < text.Length && (char.IsWhiteSpace(text[cursor]) || text[cursor] == '\0'))
            {
                cursor++;
            }
        }

        private static bool TryReadPdfInteger(string text, ref int cursor, out int value)
        {
            value = -1;
            if (!TryReadPdfToken(text, ref cursor, out string token))
            {
                return false;
            }

            return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadPdfToken(string text, ref int cursor, out string token)
        {
            token = null!;
            SkipPdfWhitespace(text, ref cursor);
            int start = cursor;
            while (cursor < text.Length && !char.IsWhiteSpace(text[cursor]) && "<>[]()/".IndexOf(text[cursor], StringComparison.Ordinal) < 0)
            {
                cursor++;
            }

            if (cursor == start)
            {
                return false;
            }

            token = text.Substring(start, cursor - start);
            return true;
        }
    }
}
