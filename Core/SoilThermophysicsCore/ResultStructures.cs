using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Common.Core;
namespace SoilThermophysics.Core
{
    /// <summary>
    /// Complete result container for soil thermal and LE simulation.
    /// Saves to categorized files matching RadSim architecture.
    /// </summary>
    [Serializable]
    public class SoilThermalSimulationResult
    {
        public string EPWCity { get; set; } = "";
        public string EPWCountry { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double TimeZone { get; set; }
        public double Elevation { get; set; }

        public int AnalyzedHours { get; set; }
        public int TotalPoints { get; set; } = 1;
        public bool IsSpatial { get; set; } = false;

        public LatentHeatMethod LatentHeatMethodUsed { get; set; } = LatentHeatMethod.Simplified;

        public SoilTempResult SoilTemp { get; set; } = new SoilTempResult();
        public LEFluxResult LEFlux { get; set; } = new LEFluxResult();
        public ETResult ET { get; set; } = new ETResult();
        public EnergyBalanceResult EnergyBalance { get; set; } = new EnergyBalanceResult();

        /// <summary>Save all results to categorized files in the specified folder.</summary>
        public void SaveToFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            SoilTemp?.SaveToFile(Path.Combine(folderPath, "SoilTempResult.txt"), this);
            LEFlux?.SaveToFile(Path.Combine(folderPath, "LEFluxResult.txt"), this);
            ET?.SaveToFile(Path.Combine(folderPath, "ETResult.txt"), this);
            EnergyBalance?.SaveToFile(Path.Combine(folderPath, "EnergyBalanceResult.txt"), this);
        }
    }

    /// <summary>Soil temperature results (Tg, T2 time series).</summary>
    [Serializable]
    public class SoilTempResult
    {
        public List<List<double>> HourlyGroundTemperature { get; set; } = new List<List<double>>();
        public List<List<double>> HourlyDeepTemperature { get; set; } = new List<List<double>>();
        public List<double> AnnualMeanTemperature { get; set; } = new List<double>();

        public void SaveToFile(string filePath, SoilThermalSimulationResult parent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SoilTempResult - Soil Temperature Output");
            sb.AppendLine($"# EPW: {parent.EPWCity}, {parent.EPWCountry}");
            sb.AppendLine($"# Lat: {parent.Latitude:F4}, Lon: {parent.Longitude:F4}");
            sb.AppendLine($"# Points: {parent.TotalPoints}, Hours: {parent.AnalyzedHours}");
            sb.AppendLine($"# LatentHeatMethod: {parent.LatentHeatMethodUsed}");
            sb.AppendLine("# Columns: Point, Hour, Tg[C], T2[C]");

            for (int p = 0; p < parent.TotalPoints; p++)
            {
                var tg = p < HourlyGroundTemperature.Count ? HourlyGroundTemperature[p] : new List<double>();
                var t2 = p < HourlyDeepTemperature.Count ? HourlyDeepTemperature[p] : new List<double>();
                int count = Math.Min(parent.AnalyzedHours, Math.Min(tg.Count, t2.Count));
                for (int h = 0; h < count; h++)
                {
                    sb.AppendLine($"{p}\t{h}\t{tg[h]:F4}\t{t2[h]:F4}");
                }
            }
            File.WriteAllText(filePath, sb.ToString());
        }
    }

    /// <summary>Latent heat flux results (LE, resistances, beta time series).</summary>
    [Serializable]
    public class LEFluxResult
    {
        public List<List<double>> HourlyLE { get; set; } = new List<List<double>>();
        public List<List<double>> HourlyBeta { get; set; } = new List<List<double>>();
        public List<List<double>> HourlyRa { get; set; } = new List<List<double>>();
        public List<List<double>> HourlyRsSoil { get; set; } = new List<List<double>>();

        public void SaveToFile(string filePath, SoilThermalSimulationResult parent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# LEFluxResult - Latent Heat Flux Output");
            sb.AppendLine($"# Points: {parent.TotalPoints}, Hours: {parent.AnalyzedHours}");
            sb.AppendLine("# Columns: Point, Hour, LE[W/m2], Beta[-], Ra[s/m], Rs_soil[s/m]");

            for (int p = 0; p < parent.TotalPoints; p++)
            {
                var le = GetList(HourlyLE, p);
                var beta = GetList(HourlyBeta, p);
                var ra = GetList(HourlyRa, p);
                var rsS = GetList(HourlyRsSoil, p);
                int count = Math.Min(parent.AnalyzedHours, le.Count);
                for (int h = 0; h < count; h++)
                {
                    sb.AppendLine($"{p}\t{h}\t{le[h]:F4}\t{beta[h]:F6}\t{ra[h]:F2}\t{rsS[h]:F2}");
                }
            }
            File.WriteAllText(filePath, sb.ToString());
        }

        private static List<double> GetList(List<List<double>> data, int index)
        {
            return index < data.Count ? data[index] : new List<double>();
        }
    }

    /// <summary>Evapotranspiration results (ET total from LE conversion, reference ET).</summary>
    [Serializable]
    public class ETResult
    {
        public List<List<double>> HourlyET { get; set; } = new List<List<double>>();
        public List<List<double>> HourlyReferenceET { get; set; } = new List<List<double>>();
        public List<double> TotalAnnualET { get; set; } = new List<double>();

        public void SaveToFile(string filePath, SoilThermalSimulationResult parent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ETResult - Evapotranspiration Output");
            sb.AppendLine($"# Points: {parent.TotalPoints}, Hours: {parent.AnalyzedHours}");
            sb.AppendLine("# Units: mm/h");
            sb.AppendLine("# Columns: Point, Hour, ET[mm/h], ETref[mm/h]");

            for (int p = 0; p < parent.TotalPoints; p++)
            {
                var et = GetList(HourlyET, p);
                var etRef = GetList(HourlyReferenceET, p);
                int count = Math.Min(parent.AnalyzedHours, et.Count);
                for (int h = 0; h < count; h++)
                {
                    sb.AppendLine($"{p}\t{h}\t{et[h]:F6}\t{etRef[h]:F6}");
                }
                double totET = et.Sum();
                sb.AppendLine($"# Point {p} Annual ET: {totET:F2}mm");
            }
            File.WriteAllText(filePath, sb.ToString());
        }

        private static List<double> GetList(List<List<double>> data, int index)
        {
            return index < data.Count ? data[index] : new List<double>();
        }
    }

    /// <summary>Energy balance results (Rn, H, LE, G four-component time series).</summary>
    [Serializable]
    public class EnergyBalanceResult
    {
        public List<List<double>> HourlyNetRadiation { get; set; } = new List<List<double>>();
        public List<List<double>> HourlySensibleHeat { get; set; } = new List<List<double>>();
        public List<List<double>> HourlyLatentHeat { get; set; } = new List<List<double>>();
        public List<List<double>> HourlyGroundHeat { get; set; } = new List<List<double>>();

        public void SaveToFile(string filePath, SoilThermalSimulationResult parent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# EnergyBalanceResult - Surface Energy Balance Output");
            sb.AppendLine($"# Points: {parent.TotalPoints}, Hours: {parent.AnalyzedHours}");
            sb.AppendLine("# Units: W/m2");
            sb.AppendLine("# Columns: Point, Hour, Rn[W/m2], H[W/m2], LE[W/m2], G[W/m2]");
            sb.AppendLine("# Note: Rn = H + LE + G (closure check)");

            for (int p = 0; p < parent.TotalPoints; p++)
            {
                var rn = GetList(HourlyNetRadiation, p);
                var h = GetList(HourlySensibleHeat, p);
                var le = GetList(HourlyLatentHeat, p);
                var g = GetList(HourlyGroundHeat, p);
                int count = Math.Min(parent.AnalyzedHours, rn.Count);
                for (int i = 0; i < count; i++)
                {
                    double closure = rn[i] - h[i] - le[i] - g[i];
                    sb.AppendLine($"{p}\t{i}\t{rn[i]:F4}\t{h[i]:F4}\t{le[i]:F4}\t{g[i]:F4}\t{closure:F6}");
                }
            }
            File.WriteAllText(filePath, sb.ToString());
        }

        private static List<double> GetList(List<List<double>> data, int index)
        {
            return index < data.Count ? data[index] : new List<double>();
        }
    }

    #region Result File Parsers (for Read* components)

    public static class SoilTempParser
    {
        public static SoilTempResult Load(string filePath)
        {
            var result = new SoilTempResult();
            if (!File.Exists(filePath)) return result;

            var tgDict = new Dictionary<int, List<double>>();
            var t2Dict = new Dictionary<int, List<double>>();

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[0], out int p)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double tg)) continue;
                if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double t2)) continue;

                if (!tgDict.ContainsKey(p)) { tgDict[p] = new List<double>(); t2Dict[p] = new List<double>(); }
                tgDict[p].Add(tg);
                t2Dict[p].Add(t2);
            }

            int maxP = tgDict.Keys.Count > 0 ? tgDict.Keys.Max() : -1;
            for (int p = 0; p <= maxP; p++)
            {
                result.HourlyGroundTemperature.Add(tgDict.ContainsKey(p) ? tgDict[p] : new List<double>());
                result.HourlyDeepTemperature.Add(t2Dict.ContainsKey(p) ? t2Dict[p] : new List<double>());
            }
            return result;
        }
    }

    public static class LEFluxParser
    {
        public static LEFluxResult Load(string filePath)
        {
            var result = new LEFluxResult();
            if (!File.Exists(filePath)) return result;

            var dict = new Dictionary<int, List<double>[]>();

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) continue;
                if (!int.TryParse(parts[0], out int p)) continue;

                if (!dict.ContainsKey(p)) dict[p] = new List<double>[4];
                for (int i = 0; i < 4; i++)
                    if (dict[p][i] == null) dict[p][i] = new List<double>();

                for (int i = 0; i < 4; i++)
                    if (double.TryParse(parts[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        dict[p][i].Add(v);
            }

            int maxP = dict.Keys.Count > 0 ? dict.Keys.Max() : -1;
            for (int p = 0; p <= maxP; p++)
            {
                var lists = dict.ContainsKey(p) ? dict[p] : new List<double>[4];
                result.HourlyLE.Add(lists[0] ?? new List<double>());
                result.HourlyBeta.Add(lists[1] ?? new List<double>());
                result.HourlyRa.Add(lists[2] ?? new List<double>());
                result.HourlyRsSoil.Add(lists[3] ?? new List<double>());
            }
            return result;
        }
    }

    public static class ETParser
    {
        public static ETResult Load(string filePath)
        {
            var result = new ETResult();
            if (!File.Exists(filePath)) return result;

            var dict = new Dictionary<int, List<double>[]>();

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                if (line.StartsWith("# Point")) continue;
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[0], out int p)) continue;

                if (!dict.ContainsKey(p)) dict[p] = new List<double>[2];
                for (int i = 0; i < 2; i++)
                    if (dict[p][i] == null) dict[p][i] = new List<double>();

                for (int i = 0; i < 2; i++)
                    if (double.TryParse(parts[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        dict[p][i].Add(v);
            }

            int maxP = dict.Keys.Count > 0 ? dict.Keys.Max() : -1;
            for (int p = 0; p <= maxP; p++)
            {
                var lists = dict.ContainsKey(p) ? dict[p] : new List<double>[2];
                result.HourlyET.Add(lists[0] ?? new List<double>());
                result.HourlyReferenceET.Add(lists[1] ?? new List<double>());
                var et = lists[0] ?? new List<double>();
                result.TotalAnnualET.Add(et.Sum());
            }
            return result;
        }
    }

    public static class EnergyBalanceParser
    {
        public static EnergyBalanceResult Load(string filePath)
        {
            var result = new EnergyBalanceResult();
            if (!File.Exists(filePath)) return result;

            var dict = new Dictionary<int, List<double>[]>();

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 7) continue;
                if (!int.TryParse(parts[0], out int p)) continue;

                if (!dict.ContainsKey(p)) dict[p] = new List<double>[4];
                for (int i = 0; i < 4; i++)
                    if (dict[p][i] == null) dict[p][i] = new List<double>();

                for (int i = 0; i < 4; i++)
                    if (double.TryParse(parts[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        dict[p][i].Add(v);
            }

            int maxP = dict.Keys.Count > 0 ? dict.Keys.Max() : -1;
            for (int p = 0; p <= maxP; p++)
            {
                var lists = dict.ContainsKey(p) ? dict[p] : new List<double>[4];
                result.HourlyNetRadiation.Add(lists[0] ?? new List<double>());
                result.HourlySensibleHeat.Add(lists[1] ?? new List<double>());
                result.HourlyLatentHeat.Add(lists[2] ?? new List<double>());
                result.HourlyGroundHeat.Add(lists[3] ?? new List<double>());
            }
            return result;
        }
    }

    #endregion
}
