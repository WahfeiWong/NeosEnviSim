using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino.Geometry;

namespace SolarPV.Core
{
    /// <summary>
    /// Serializable container for all radiation simulation results produced by NeosRadSim.
    /// Results are split into 6 categories matching the output file structure.
    /// </summary>
    [Serializable]
    public class RadiationSimulationResult
    {
        // Metadata
        public string EPWCity { get; set; } = "";
        public string EPWCountry { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double TimeZone { get; set; }
        public double Elevation { get; set; }
        public int AnalyzedHours { get; set; }
        public int TotalFaces { get; set; }
        public int PanelCount { get; set; }
        public bool BifacialEnabled { get; set; }
        public bool PerezModelUsed { get; set; }
        public bool InverterModelUsed { get; set; }
        public bool TemperatureModelUsed { get; set; }
        public double SystemLossFactor { get; set; }
        public string SimulationTimestamp { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 1. Geometry Result
        public GeometryResult Geometry { get; set; } = new GeometryResult();

        // 2. View Result
        public ViewResult View { get; set; } = new ViewResult();

        // 3. Sunshine Duration & Radiation Result
        public SunRadiationResult SunRadiation { get; set; } = new SunRadiationResult();

        // 4. PV System Info
        public PVInfoResult PVInfo { get; set; } = new PVInfoResult();

        // 5. DC Generation Result
        public DCResult DC { get; set; } = new DCResult();

        // 6. AC Generation Result
        public ACResult AC { get; set; } = new ACResult();

        #region Save / Load

        public void SaveToFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            SaveGeometryResult(Path.Combine(folderPath, "GeometryResult.txt"));
            SaveViewResult(Path.Combine(folderPath, "ViewResult.txt"));
            SaveSunRadiationResult(Path.Combine(folderPath, "SunRadiationResult.txt"));
            SavePVInfoResult(Path.Combine(folderPath, "PVInfoResult.txt"));
            SaveDCResult(Path.Combine(folderPath, "DCResult.txt"));
            SaveACResult(Path.Combine(folderPath, "ACResult.txt"));
            SaveMetadata(Path.Combine(folderPath, "SimulationMetadata.txt"));
        }

        private void SaveMetadata(string path)
        {
            var lines = new List<string>
            {
                $"EPWCity:{EPWCity}",
                $"EPWCountry:{EPWCountry}",
                $"Latitude:{Latitude:F6}",
                $"Longitude:{Longitude:F6}",
                $"TimeZone:{TimeZone:F2}",
                $"Elevation:{Elevation:F1}",
                $"AnalyzedHours:{AnalyzedHours}",
                $"TotalFaces:{TotalFaces}",
                $"PanelCount:{PanelCount}",
                $"BifacialEnabled:{BifacialEnabled}",
                $"PerezModelUsed:{PerezModelUsed}",
                $"InverterModelUsed:{InverterModelUsed}",
                $"TemperatureModelUsed:{TemperatureModelUsed}",
                $"SystemLossFactor:{SystemLossFactor:F4}",
                $"Timestamp:{SimulationTimestamp}"
            };
            File.WriteAllLines(path, lines);
        }

        private void SaveGeometryResult(string path)
        {
            var lines = new List<string>();
            lines.Add($"PanelCount:{Geometry.PanelCount}");
            lines.Add($"TotalFaces:{Geometry.TotalFaces}");
            lines.Add($"SunVectorsCount:{Geometry.SunVectors.Count}");

            lines.Add("[FaceCenters]");
            foreach (var panel in Geometry.FaceCenters)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var pt in panel)
                    lines.Add($"{pt.X:F6},{pt.Y:F6},{pt.Z:F6}");
            }

            lines.Add("[FaceNormals]");
            foreach (var panel in Geometry.FaceNormals)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v.X:F6},{v.Y:F6},{v.Z:F6}");
            }

            lines.Add("[FaceAreas]");
            foreach (var panel in Geometry.FaceAreas)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var a in panel)
                    lines.Add($"{a:F6}");
            }

            lines.Add("[TiltAngles]");
            foreach (var panel in Geometry.TiltAngles)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var t in panel)
                    lines.Add($"{t:F6}");
            }

            lines.Add("[SunVectors]");
            foreach (var sv in Geometry.SunVectors)
                lines.Add($"{sv.X:F6},{sv.Y:F6},{sv.Z:F6}");

            lines.Add("[Meshes]");
            foreach (var meshData in Geometry.Meshes)
            {
                lines.Add($"M:VC={meshData.VertexCoordinates.Count},FC={meshData.FaceIndices.Count}");
                foreach (var vc in meshData.VertexCoordinates)
                    lines.Add($"V:{vc[0]:F6},{vc[1]:F6},{vc[2]:F6}");
                foreach (var fc in meshData.FaceIndices)
                    lines.Add($"F:{fc[0]},{fc[1]},{fc[2]},{fc[3]}");
            }

            File.WriteAllLines(path, lines);
        }

        private void SaveViewResult(string path)
        {
            var lines = new List<string>();
            lines.Add($"TotalFaces:{View.TotalFaces}");
            lines.Add($"PanelCount:{View.PanelCount}");

            lines.Add("[FrontSVF]");
            foreach (var panel in View.FrontSVF)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[RearSVF]");
            foreach (var panel in View.RearSVF)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[FrontObstacleViewFactor]");
            foreach (var panel in View.FrontObstacleViewFactor)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[RearObstacleViewFactor]");
            foreach (var panel in View.RearObstacleViewFactor)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            // ENHANCED (2026-06-16): Save TVF and TRVF
            lines.Add("[FrontTVF]");
            foreach (var panel in View.FrontTVF)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[RearTVF]");
            foreach (var panel in View.RearTVF)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[FrontTRVF]");
            foreach (var panel in View.FrontTRVF)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[RearTRVF]");
            foreach (var panel in View.RearTRVF)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            File.WriteAllLines(path, lines);
        }

        private void SaveSunRadiationResult(string path)
        {
            var lines = new List<string>();
            lines.Add($"TotalFaces:{SunRadiation.TotalFaces}");
            lines.Add($"HourCount:{SunRadiation.HourCount}");
            lines.Add($"PanelCount:{SunRadiation.PanelCount}");
            lines.Add($"BifacialEnabled:{SunRadiation.BifacialEnabled}");

            lines.Add("[FrontSunshineHours]");
            foreach (var panel in SunRadiation.FrontSunshineHours)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F2}");
            }

            lines.Add("[RearSunshineHours]");
            foreach (var panel in SunRadiation.RearSunshineHours)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F2}");
            }

            lines.Add("[HourlyFrontRadiation]");
            foreach (var panel in SunRadiation.HourlyFrontRadiation)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var faceHours in panel)
                {
                    lines.Add(string.Join(",", faceHours.Select(h => h.ToString("F4"))));
                }
            }

            lines.Add("[HourlyRearRadiation]");
            foreach (var panel in SunRadiation.HourlyRearRadiation)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var faceHours in panel)
                {
                    lines.Add(string.Join(",", faceHours.Select(h => h.ToString("F4"))));
                }
            }

            lines.Add("[TotalFrontRadiation]");
            foreach (var panel in SunRadiation.TotalFrontRadiation)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F4}");
            }

            lines.Add("[TotalRearRadiation]");
            foreach (var panel in SunRadiation.TotalRearRadiation)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F4}");
            }

            File.WriteAllLines(path, lines);
        }

        private void SavePVInfoResult(string path)
        {
            var lines = new List<string>();
            lines.Add($"HourCount:{PVInfo.HourCount}");
            lines.Add($"TotalAnnualClippingLoss:{PVInfo.TotalAnnualClippingLoss:F4}");
            lines.Add($"TotalAnnualInverterLoss:{PVInfo.TotalAnnualInverterLoss:F4}");

            lines.Add("[HourlyCellTemperatures]");
            lines.Add(string.Join(",", PVInfo.HourlyCellTemperatures.Select(t => t.ToString("F4"))));

            lines.Add("[HourlyEffectiveIrradiance]");
            lines.Add(string.Join(",", PVInfo.HourlyEffectiveIrradiance.Select(i => i.ToString("F4"))));

            lines.Add("[HourlyAmbientTemperatures]");
            lines.Add(string.Join(",", PVInfo.HourlyAmbientTemperatures.Select(t => t.ToString("F4"))));

            lines.Add("[HourlyWindSpeeds]");
            lines.Add(string.Join(",", PVInfo.HourlyWindSpeeds.Select(w => w.ToString("F4"))));

            File.WriteAllLines(path, lines);
        }

        private void SaveDCResult(string path)
        {
            var lines = new List<string>();
            lines.Add($"TotalFaces:{DC.TotalFaces}");
            lines.Add($"HourCount:{DC.HourCount}");
            lines.Add($"PanelCount:{DC.PanelCount}");
            lines.Add($"BifacialEnabled:{DC.BifacialEnabled}");
            lines.Add($"TotalAnnualFrontDC:{DC.TotalAnnualFrontDC:F4}");
            lines.Add($"TotalAnnualRearDC:{DC.TotalAnnualRearDC:F4}");
            lines.Add($"TotalAnnualDC:{DC.TotalAnnualDC:F4}");

            lines.Add("[FrontDCTotalPerFace]");
            foreach (var panel in DC.FrontDCTotalPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[RearDCTotalPerFace]");
            foreach (var panel in DC.RearDCTotalPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[HourlyFrontDCPerFace]");
            foreach (var panel in DC.HourlyFrontDCPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var faceHours in panel)
                {
                    lines.Add(string.Join(",", faceHours.Select(h => h.ToString("F6"))));
                }
            }

            lines.Add("[HourlyRearDCPerFace]");
            foreach (var panel in DC.HourlyRearDCPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var faceHours in panel)
                {
                    lines.Add(string.Join(",", faceHours.Select(h => h.ToString("F6"))));
                }
            }

            lines.Add("[HourlyTotalDC]");
            lines.Add(string.Join(",", DC.HourlyTotalDC.Select(h => h.ToString("F6"))));

            File.WriteAllLines(path, lines);
        }

        private void SaveACResult(string path)
        {
            var lines = new List<string>();
            lines.Add($"TotalFaces:{AC.TotalFaces}");
            lines.Add($"HourCount:{AC.HourCount}");
            lines.Add($"PanelCount:{AC.PanelCount}");
            lines.Add($"BifacialEnabled:{AC.BifacialEnabled}");
            lines.Add($"TotalAnnualFrontAC:{AC.TotalAnnualFrontAC:F4}");
            lines.Add($"TotalAnnualRearAC:{AC.TotalAnnualRearAC:F4}");
            lines.Add($"TotalAnnualAC:{AC.TotalAnnualAC:F4}");

            lines.Add("[FrontACTotalPerFace]");
            foreach (var panel in AC.FrontACTotalPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[RearACTotalPerFace]");
            foreach (var panel in AC.RearACTotalPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var v in panel)
                    lines.Add($"{v:F6}");
            }

            lines.Add("[HourlyFrontACPerFace]");
            foreach (var panel in AC.HourlyFrontACPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var faceHours in panel)
                {
                    lines.Add(string.Join(",", faceHours.Select(h => h.ToString("F6"))));
                }
            }

            lines.Add("[HourlyRearACPerFace]");
            foreach (var panel in AC.HourlyRearACPerFace)
            {
                lines.Add($"P:{panel.Count}");
                foreach (var faceHours in panel)
                {
                    lines.Add(string.Join(",", faceHours.Select(h => h.ToString("F6"))));
                }
            }

            lines.Add("[HourlyTotalAC]");
            lines.Add(string.Join(",", AC.HourlyTotalAC.Select(h => h.ToString("F6"))));

            File.WriteAllLines(path, lines);
        }

        #endregion
    }

    #region Result Sub-Containers

    [Serializable]
    public class GeometryResult
    {
        public int PanelCount { get; set; }
        public int TotalFaces { get; set; }
        public List<List<Point3d>> FaceCenters { get; set; } = new List<List<Point3d>>();
        public List<List<Vector3d>> FaceNormals { get; set; } = new List<List<Vector3d>>();
        public List<List<double>> FaceAreas { get; set; } = new List<List<double>>();
        public List<List<double>> TiltAngles { get; set; } = new List<List<double>>();
        public List<Vector3d> SunVectors { get; set; } = new List<Vector3d>();
        public List<MeshData> Meshes { get; set; } = new List<MeshData>();
    }

    [Serializable]
    public class MeshData
    {
        public List<double[]> VertexCoordinates { get; set; } = new List<double[]>();
        public List<int[]> FaceIndices { get; set; } = new List<int[]>();
    }

    [Serializable]
    public class ViewResult
    {
        public int TotalFaces { get; set; }
        public int PanelCount { get; set; }
        public List<List<double>> FrontSVF { get; set; } = new List<List<double>>();
        public List<List<double>> RearSVF { get; set; } = new List<List<double>>();
        /// <summary>ENHANCED (2026-06-16): OVF is now OPAQUE-ONLY (previously included all obstacles).</summary>
        public List<List<double>> FrontObstacleViewFactor { get; set; } = new List<List<double>>();
        /// <summary>ENHANCED (2026-06-16): OVF is now OPAQUE-ONLY (previously included all obstacles).</summary>
        public List<List<double>> RearObstacleViewFactor { get; set; } = new List<List<double>>();
        /// <summary>ENHANCED (2026-06-16): Tree View Factor - directions blocked by tree detail meshes.</summary>
        public List<List<double>> FrontTVF { get; set; } = new List<List<double>>();
        /// <summary>ENHANCED (2026-06-16): Rear-side Tree View Factor.</summary>
        public List<List<double>> RearTVF { get; set; } = new List<List<double>>();
        /// <summary>ENHANCED (2026-06-16): Translucent View Factor - directions blocked by translucent shade meshes.</summary>
        public List<List<double>> FrontTRVF { get; set; } = new List<List<double>>();
        /// <summary>ENHANCED (2026-06-16): Rear-side Translucent View Factor.</summary>
        public List<List<double>> RearTRVF { get; set; } = new List<List<double>>();
    }

    [Serializable]
    public class SunRadiationResult
    {
        public int TotalFaces { get; set; }
        public int HourCount { get; set; }
        public int PanelCount { get; set; }
        public bool BifacialEnabled { get; set; }
        public List<List<double>> FrontSunshineHours { get; set; } = new List<List<double>>();
        public List<List<double>> RearSunshineHours { get; set; } = new List<List<double>>();
        public List<List<List<double>>> HourlyFrontRadiation { get; set; } = new List<List<List<double>>>();
        public List<List<List<double>>> HourlyRearRadiation { get; set; } = new List<List<List<double>>>();
        public List<List<double>> TotalFrontRadiation { get; set; } = new List<List<double>>();
        public List<List<double>> TotalRearRadiation { get; set; } = new List<List<double>>();
    }

    [Serializable]
    public class PVInfoResult
    {
        public int HourCount { get; set; }
        public List<double> HourlyCellTemperatures { get; set; } = new List<double>();
        public List<double> HourlyEffectiveIrradiance { get; set; } = new List<double>();
        public List<double> HourlyAmbientTemperatures { get; set; } = new List<double>();
        public List<double> HourlyWindSpeeds { get; set; } = new List<double>();
        public double TotalAnnualClippingLoss { get; set; }
        public double TotalAnnualInverterLoss { get; set; }
    }

    [Serializable]
    public class DCResult
    {
        public int TotalFaces { get; set; }
        public int HourCount { get; set; }
        public int PanelCount { get; set; }
        public bool BifacialEnabled { get; set; }
        public double TotalAnnualFrontDC { get; set; }
        public double TotalAnnualRearDC { get; set; }
        public double TotalAnnualDC { get; set; }
        public List<List<double>> FrontDCTotalPerFace { get; set; } = new List<List<double>>();
        public List<List<double>> RearDCTotalPerFace { get; set; } = new List<List<double>>();
        public List<List<List<double>>> HourlyFrontDCPerFace { get; set; } = new List<List<List<double>>>();
        public List<List<List<double>>> HourlyRearDCPerFace { get; set; } = new List<List<List<double>>>();
        public List<double> HourlyTotalDC { get; set; } = new List<double>();
    }

    [Serializable]
    public class ACResult
    {
        public int TotalFaces { get; set; }
        public int HourCount { get; set; }
        public int PanelCount { get; set; }
        public bool BifacialEnabled { get; set; }
        public double TotalAnnualFrontAC { get; set; }
        public double TotalAnnualRearAC { get; set; }
        public double TotalAnnualAC { get; set; }
        public List<List<double>> FrontACTotalPerFace { get; set; } = new List<List<double>>();
        public List<List<double>> RearACTotalPerFace { get; set; } = new List<List<double>>();
        public List<List<List<double>>> HourlyFrontACPerFace { get; set; } = new List<List<List<double>>>();
        public List<List<List<double>>> HourlyRearACPerFace { get; set; } = new List<List<List<double>>>();
        public List<double> HourlyTotalAC { get; set; } = new List<double>();
    }

    #endregion
}
