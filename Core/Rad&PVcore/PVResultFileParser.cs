using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace SolarPV.Core
{
    /// <summary>
    /// Lightweight parser for NeosRadSim result files.
    /// Each public method loads one category from its corresponding text file.
    /// </summary>
    public static class ResultFileParser
    {
        public static GeometryResult LoadGeometryResult(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            var result = new GeometryResult();
            int idx = 0;

            result.PanelCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.TotalFaces = ParseInt(ParseKeyValue(lines[idx++]).Value);
            int sunVecCount = ParseInt(ParseKeyValue(lines[idx++]).Value);

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line == "[FaceCenters]")
                {
                    idx++;
                    result.FaceCenters = ReadPoint3dPanels(ref idx, lines);
                }
                else if (line == "[FaceNormals]")
                {
                    idx++;
                    result.FaceNormals = ReadVector3dPanels(ref idx, lines);
                }
                else if (line == "[FaceAreas]")
                {
                    idx++;
                    result.FaceAreas = ReadDoublePanels(ref idx, lines);
                }
                else if (line == "[TiltAngles]")
                {
                    idx++;
                    result.TiltAngles = ReadDoublePanels(ref idx, lines);
                }
                else if (line == "[SunVectors]")
                {
                    idx++;
                    result.SunVectors = ReadVector3dList(ref idx, lines, sunVecCount);
                }
                else if (line == "[Meshes]")
                {
                    idx++;
                    result.Meshes = ReadMeshDataList(ref idx, lines);
                }
                else idx++;
            }
            return result;
        }

        public static ViewResult LoadViewResult(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            var result = new ViewResult();
            int idx = 0;
            result.TotalFaces = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.PanelCount = ParseInt(ParseKeyValue(lines[idx++]).Value);

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line == "[FrontSVF]") { idx++; result.FrontSVF = ReadDoublePanels(ref idx, lines); }
                else if (line == "[RearSVF]") { idx++; result.RearSVF = ReadDoublePanels(ref idx, lines); }
                else if (line == "[FrontObstacleViewFactor]") { idx++; result.FrontObstacleViewFactor = ReadDoublePanels(ref idx, lines); }
                else if (line == "[RearObstacleViewFactor]") { idx++; result.RearObstacleViewFactor = ReadDoublePanels(ref idx, lines); }
                else idx++;
            }
            return result;
        }

        public static SunRadiationResult LoadSunRadiationResult(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            var result = new SunRadiationResult();
            int idx = 0;
            result.TotalFaces = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.HourCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.PanelCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.BifacialEnabled = bool.Parse(ParseKeyValue(lines[idx++]).Value);

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line == "[FrontSunshineHours]") { idx++; result.FrontSunshineHours = ReadDoublePanels(ref idx, lines); }
                else if (line == "[RearSunshineHours]") { idx++; result.RearSunshineHours = ReadDoublePanels(ref idx, lines); }
                else if (line == "[HourlyFrontRadiation]") { idx++; result.HourlyFrontRadiation = ReadDoubleListPanels(ref idx, lines); }
                else if (line == "[HourlyRearRadiation]") { idx++; result.HourlyRearRadiation = ReadDoubleListPanels(ref idx, lines); }
                else if (line == "[TotalFrontRadiation]") { idx++; result.TotalFrontRadiation = ReadDoublePanels(ref idx, lines); }
                else if (line == "[TotalRearRadiation]") { idx++; result.TotalRearRadiation = ReadDoublePanels(ref idx, lines); }
                else idx++;
            }
            return result;
        }

        public static PVInfoResult LoadPVInfoResult(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            var result = new PVInfoResult();
            int idx = 0;
            result.HourCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualClippingLoss = ParseDouble(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualInverterLoss = ParseDouble(ParseKeyValue(lines[idx++]).Value);

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line == "[HourlyCellTemperatures]") { idx++; result.HourlyCellTemperatures = ParseCommaLine(lines[idx++]); }
                else if (line == "[HourlyEffectiveIrradiance]") { idx++; result.HourlyEffectiveIrradiance = ParseCommaLine(lines[idx++]); }
                else if (line == "[HourlyAmbientTemperatures]") { idx++; result.HourlyAmbientTemperatures = ParseCommaLine(lines[idx++]); }
                else if (line == "[HourlyWindSpeeds]") { idx++; result.HourlyWindSpeeds = ParseCommaLine(lines[idx++]); }
                else idx++;
            }
            return result;
        }

        public static DCResult LoadDCResult(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            var result = new DCResult();
            int idx = 0;
            result.TotalFaces = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.HourCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.PanelCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.BifacialEnabled = bool.Parse(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualFrontDC = ParseDouble(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualRearDC = ParseDouble(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualDC = ParseDouble(ParseKeyValue(lines[idx++]).Value);

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line == "[FrontDCTotalPerFace]") { idx++; result.FrontDCTotalPerFace = ReadDoublePanels(ref idx, lines); }
                else if (line == "[RearDCTotalPerFace]") { idx++; result.RearDCTotalPerFace = ReadDoublePanels(ref idx, lines); }
                else if (line == "[HourlyFrontDCPerFace]") { idx++; result.HourlyFrontDCPerFace = ReadDoubleListPanels(ref idx, lines); }
                else if (line == "[HourlyRearDCPerFace]") { idx++; result.HourlyRearDCPerFace = ReadDoubleListPanels(ref idx, lines); }
                else if (line == "[HourlyTotalDC]") { idx++; result.HourlyTotalDC = ParseCommaLine(lines[idx++]); }
                else idx++;
            }
            return result;
        }

        public static ACResult LoadACResult(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            var result = new ACResult();
            int idx = 0;
            result.TotalFaces = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.HourCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.PanelCount = ParseInt(ParseKeyValue(lines[idx++]).Value);
            result.BifacialEnabled = bool.Parse(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualFrontAC = ParseDouble(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualRearAC = ParseDouble(ParseKeyValue(lines[idx++]).Value);
            result.TotalAnnualAC = ParseDouble(ParseKeyValue(lines[idx++]).Value);

            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line == "[FrontACTotalPerFace]") { idx++; result.FrontACTotalPerFace = ReadDoublePanels(ref idx, lines); }
                else if (line == "[RearACTotalPerFace]") { idx++; result.RearACTotalPerFace = ReadDoublePanels(ref idx, lines); }
                else if (line == "[HourlyFrontACPerFace]") { idx++; result.HourlyFrontACPerFace = ReadDoubleListPanels(ref idx, lines); }
                else if (line == "[HourlyRearACPerFace]") { idx++; result.HourlyRearACPerFace = ReadDoubleListPanels(ref idx, lines); }
                else if (line == "[HourlyTotalAC]") { idx++; result.HourlyTotalAC = ParseCommaLine(lines[idx++]); }
                else idx++;
            }
            return result;
        }

        #region Helpers

        private static KeyValuePair<string, string> ParseKeyValue(string line)
        {
            var parts = line.Split(new[] { ':' }, 2);
            return new KeyValuePair<string, string>(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
        }

        private static int ParseInt(string s) => int.TryParse(s, out int v) ? v : 0;
        private static double ParseDouble(string s) => double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static List<double> ParseCommaLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return new List<double>();
            return line.Split(',').Select(s => ParseDouble(s.Trim())).ToList();
        }

        private static List<List<Point3d>> ReadPoint3dPanels(ref int idx, string[] lines)
        {
            var panels = new List<List<Point3d>>();
            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) break;
                if (line.StartsWith("P:"))
                {
                    int count = ParseInt(line.Substring(2));
                    var panel = new List<Point3d>(count);
                    idx++;
                    for (int i = 0; i < count && idx < lines.Length; i++, idx++)
                    {
                        var parts = lines[idx].Split(',');
                        panel.Add(new Point3d(ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[2])));
                    }
                    panels.Add(panel);
                }
                else idx++;
            }
            return panels;
        }

        private static List<List<Vector3d>> ReadVector3dPanels(ref int idx, string[] lines)
        {
            var panels = new List<List<Vector3d>>();
            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) break;
                if (line.StartsWith("P:"))
                {
                    int count = ParseInt(line.Substring(2));
                    var panel = new List<Vector3d>(count);
                    idx++;
                    for (int i = 0; i < count && idx < lines.Length; i++, idx++)
                    {
                        var parts = lines[idx].Split(',');
                        panel.Add(new Vector3d(ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[2])));
                    }
                    panels.Add(panel);
                }
                else idx++;
            }
            return panels;
        }

        private static List<Vector3d> ReadVector3dList(ref int idx, string[] lines, int expectedCount)
        {
            var list = new List<Vector3d>(expectedCount);
            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) break;
                var parts = line.Split(',');
                if (parts.Length >= 3)
                    list.Add(new Vector3d(ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[2])));
                idx++;
            }
            return list;
        }

        private static List<List<double>> ReadDoublePanels(ref int idx, string[] lines)
        {
            var panels = new List<List<double>>();
            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) break;
                if (line.StartsWith("P:"))
                {
                    int count = ParseInt(line.Substring(2));
                    var panel = new List<double>(count);
                    idx++;
                    for (int i = 0; i < count && idx < lines.Length; i++, idx++)
                        panel.Add(ParseDouble(lines[idx].Trim()));
                    panels.Add(panel);
                }
                else idx++;
            }
            return panels;
        }

        private static List<List<List<double>>> ReadDoubleListPanels(ref int idx, string[] lines)
        {
            var panels = new List<List<List<double>>>();
            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) break;
                if (line.StartsWith("P:"))
                {
                    int count = ParseInt(line.Substring(2));
                    var panel = new List<List<double>>(count);
                    idx++;
                    for (int i = 0; i < count && idx < lines.Length; i++, idx++)
                    {
                        panel.Add(ParseCommaLine(lines[idx]));
                    }
                    panels.Add(panel);
                }
                else idx++;
            }
            return panels;
        }

        private static List<MeshData> ReadMeshDataList(ref int idx, string[] lines)
        {
            var meshes = new List<MeshData>();
            while (idx < lines.Length)
            {
                string line = lines[idx].Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) break;
                if (line.StartsWith("M:"))
                {
                    var mdata = new MeshData();
                    // Parse M:VC=...,FC=...
                    var kv = line.Substring(2).Split(',');
                    int vc = 0, fc = 0;
                    foreach (var part in kv)
                    {
                        var p = part.Split('=');
                        if (p[0].Trim() == "VC") vc = ParseInt(p[1]);
                        if (p[0].Trim() == "FC") fc = ParseInt(p[1]);
                    }
                    idx++;
                    for (int i = 0; i < vc && idx < lines.Length; i++, idx++)
                    {
                        var vline = lines[idx].Trim();
                        if (!vline.StartsWith("V:")) break;
                        var parts = vline.Substring(2).Split(',');
                        mdata.VertexCoordinates.Add(new[] { ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[2]) });
                    }
                    for (int i = 0; i < fc && idx < lines.Length; i++, idx++)
                    {
                        var fline = lines[idx].Trim();
                        if (!fline.StartsWith("F:")) break;
                        var parts = fline.Substring(2).Split(',');
                        mdata.FaceIndices.Add(new[] { ParseInt(parts[0]), ParseInt(parts[1]), ParseInt(parts[2]), ParseInt(parts[3]) });
                    }
                    meshes.Add(mdata);
                }
                else idx++;
            }
            return meshes;
        }

        #endregion
    }
}
