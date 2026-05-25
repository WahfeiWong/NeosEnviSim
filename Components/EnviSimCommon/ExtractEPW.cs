using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EnviSimCommonComponent
{
    /// <summary>
    /// Extract EPW component. Reads an EPW file and outputs station information and hourly meteorological data.
    /// 
    /// EPW hour alignment (matching ladybug EPW behavior):
    /// - EPW file runs from Hour=1 (00:00-01:00) to Hour=24 (23:00-24:00) on Dec 31.
    /// - For point-in-time fields (temperature, humidity, wind, pressure, sky cover, etc.),
    ///   the last record (Dec 31 24:00 = Jan 1 0:00) is moved to the start of the sequence.
    ///   This ensures HOY=0 corresponds to Jan 1 0:00 (matching ladybug's hourly collection alignment).
    /// - For radiation/illuminance fields (GHI, DNI, DHI, HIR, etc.), NO reordering is applied.
    ///   These represent accumulation/average over the previous hour, so HOY=0 corresponds to
    ///   the EPW file's first record (Jan 1 1:00, i.e., accumulation during 00:00-01:00).
    /// </summary>
    public class ExtractEPWComponent : GH_Component
    {
        public ExtractEPWComponent()
          : base("Extract EPW", "ExtEPW",
              "Extracts fixed station information and hourly meteorological data from an EPW file. " +
              "Outputs weather fields (temperature, humidity, radiation, wind, etc.) corresponding to the input HOY list. " +
              "If no HOY is provided, outputs full year data (8760 hours). " +
              "Also extracts monthly ground temperatures at various depths from the GROUND TEMPERATURES section if present. " +
              "Uses standard EnergyPlus EPW column mapping with ladybug-compatible hour alignment.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("EPW Path", "EPW", "Full file path to the .epw weather file", GH_ParamAccess.item);
            pManager.AddIntegerParameter("HOY", "HOY",
                "List of Hours-of-Year (0-based, 0-8759) to extract. If empty or not connected, defaults to full year (0-8759).",
                GH_ParamAccess.list);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("EPW Info", "Info",
                "Fixed station information from EPW header: City, State, Country, Source, WMO, Latitude, Longitude, TimeZone, Elevation",
                GH_ParamAccess.item);
            pManager.AddNumberParameter("Dry Bulb Temperature", "Tdb",
                "Dry bulb temperature [C] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Dew Point Temperature", "Tdp",
                "Dew point temperature [C] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Relative Humidity", "RH",
                "Relative humidity [%] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Atmospheric Pressure", "P_atm",
                "Atmospheric station pressure [Pa] for each HOY (accumulated, NO hour-reordering)", GH_ParamAccess.list);
            pManager.AddNumberParameter("GHI", "GHI",
                "Global horizontal irradiance [Wh/m2] for each HOY (accumulated over previous hour, NO hour-reordering)", GH_ParamAccess.list);
            pManager.AddNumberParameter("DNI", "DNI",
                "Direct normal irradiance [Wh/m2] for each HOY (accumulated over previous hour, NO hour-reordering)", GH_ParamAccess.list);
            pManager.AddNumberParameter("DHI", "DHI",
                "Diffuse horizontal irradiance [Wh/m2] for each HOY (accumulated over previous hour, NO hour-reordering)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Extraterrestrial Horizontal Radiation", "ExtHoriz",
                "Extraterrestrial horizontal radiation [Wh/m2] for each HOY (accumulated, NO hour-reordering)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Extraterrestrial Direct Normal Radiation", "ExtDNI",
                "Extraterrestrial direct normal radiation [Wh/m2] for each HOY (accumulated, NO hour-reordering)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Horizontal Infrared Radiation", "HIR",
                "Horizontal infrared radiation intensity [Wh/m2] for each HOY (accumulated, NO hour-reordering)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wind Speed", "WS",
                "Wind speed [m/s] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wind Direction", "WD",
                "Wind direction [degrees] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Total Sky Cover", "TSC",
                "Total sky cover [tenths] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Opaque Sky Cover", "OSC",
                "Opaque sky cover [tenths] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Visibility", "Vis",
                "Visibility [km] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Ceiling Height", "Ceil",
                "Ceiling height [m] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Precipitable Water", "PrecipW",
                "Precipitable water [mm] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Aerosol Optical Depth", "AOD",
                "Aerosol optical depth [thousandths] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Snow Depth", "Snow",
                "Snow depth [cm] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Albedo", "Alb",
                "Albedo [-] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Liquid Precipitation Depth", "PrecipD",
                "Liquid precipitation depth [mm] for each HOY (point-in-time, hour-aligned)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Ground Temps", "GrndTmp",
                "Monthly ground temperatures at various depths (0.5m\u30012m\u30014m). " +
                "Each branch corresponds to one depth (Path = depth index), containing 12 monthly values [C] (Jan-Dec). " +
                "Extracted from EPW GROUND TEMPERATURES section if present; otherwise the tree will be empty.",
                GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string epwPath = "";
            if (!DA.GetData(0, ref epwPath)) return;

            List<int> hoyList = new List<int>();
            bool hoyConnected = DA.GetDataList(1, hoyList);

            // Default to full year if no HOY provided or not connected
            if (!hoyConnected || hoyList == null || hoyList.Count == 0)
            {
                hoyList = Enumerable.Range(0, 8760).ToList();
            }

            // Validate HOY range
            for (int i = 0; i < hoyList.Count; i++)
            {
                if (hoyList[i] < 0) hoyList[i] = 0;
                if (hoyList[i] > 8759) hoyList[i] = 8759;
            }

            if (!File.Exists(epwPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "EPW file not found: " + epwPath);
                return;
            }

            // Parse EPW file
            string epwInfo = "";
            List<string> dataLines = new List<string>(8760);
            GH_Structure<GH_Number> groundTempTree = new GH_Structure<GH_Number>();

            List<double> tdb = new List<double>();
            List<double> tdp = new List<double>();
            List<double> rh = new List<double>();
            List<double> patm = new List<double>();
            List<double> ghi = new List<double>();
            List<double> dni = new List<double>();
            List<double> dhi = new List<double>();
            List<double> extHoriz = new List<double>();
            List<double> extDni = new List<double>();
            List<double> hir = new List<double>();
            List<double> windSpeed = new List<double>();
            List<double> windDir = new List<double>();
            List<double> totalSky = new List<double>();
            List<double> opaqueSky = new List<double>();
            List<double> visibility = new List<double>();
            List<double> ceiling = new List<double>();
            List<double> precipWater = new List<double>();
            List<double> aerosol = new List<double>();
            List<double> snowDepth = new List<double>();
            List<double> albedo = new List<double>();
            List<double> precipDepth = new List<double>();

            try
            {
                var lines = File.ReadAllLines(epwPath);
                bool dataStarted = false;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    // Parse LOCATION header
                    if (line.StartsWith("LOCATION", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(',');
                        // Standard EPW LOCATION format:
                        // 0:LOCATION, 1:City, 2:State, 3:Country, 4:Source, 5:WMO,
                        // 6:Latitude, 7:Longitude, 8:TimeZone, 9:Elevation, 10:ElevationUnit
                        string city = parts.Length > 1 ? parts[1].Trim() : "Unknown";
                        string state = parts.Length > 2 ? parts[2].Trim() : "";
                        string country = parts.Length > 3 ? parts[3].Trim() : "";
                        string source = parts.Length > 4 ? parts[4].Trim() : "";
                        string wmo = parts.Length > 5 ? parts[5].Trim() : "";
                        string lat = parts.Length > 6 ? parts[6].Trim() : "";
                        string lon = parts.Length > 7 ? parts[7].Trim() : "";
                        string tz = parts.Length > 8 ? parts[8].Trim() : "";
                        string elev = parts.Length > 9 ? parts[9].Trim() : "";
                        string elevUnit = parts.Length > 10 ? parts[10].Trim() : "m";
                        epwInfo = $"City={city}; State={state}; Country={country}; Source={source}; WMO={wmo}; " +
                                  $"Latitude={lat}; Longitude={lon}; TimeZone={tz}; Elevation={elev}{elevUnit}";
                        continue;
                    }

                    // Parse GROUND TEMPERATURES section
                    // All depths data are on the SAME line. Format per depth (16 cols each):
                    // depth, conductivity, density, specificHeat, Jan, Feb, ..., Dec
                    if (line.StartsWith("GROUND TEMPERATURES", StringComparison.OrdinalIgnoreCase))
                    {
                        var gtParts = line.Split(',');
                        if (gtParts.Length >= 2 && int.TryParse(gtParts[1].Trim(), out int nDepths) && nDepths > 0)
                        {
                            int stInd = 2; // data starts at column index 2
                            for (int d = 0; d < nDepths; d++)
                            {
                                // Each depth block: 4 metadata + 12 months = 16 columns
                                if (stInd + 15 < gtParts.Length)
                                {
                                    GH_Path path = new GH_Path(d);
                                    for (int m = 0; m < 12; m++)
                                    {
                                        int col = stInd + 4 + m; // month columns: stInd+4 to stInd+15
                                        if (double.TryParse(gtParts[col].Trim(), System.Globalization.NumberStyles.Any,
                                            System.Globalization.CultureInfo.InvariantCulture, out double temp))
                                        {
                                            groundTempTree.Append(new GH_Number(temp), path);
                                        }
                                        else
                                        {
                                            groundTempTree.Append(new GH_Number(0.0), path);
                                        }
                                    }
                                }
                                stInd += 16; // advance to next depth block
                            }
                        }
                        continue;
                    }

                    // Mark start of hourly data
                    if (line.StartsWith("DATA PERIODS", StringComparison.OrdinalIgnoreCase))
                    {
                        dataStarted = true;
                        continue;
                    }

                    if (dataStarted)
                    {
                        dataLines.Add(line);
                    }
                }

                // Fallback if no DATA PERIODS header found
                if (!dataStarted && dataLines.Count == 0)
                {
                    bool inHeader = true;
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (inHeader)
                        {
                            if (char.IsDigit(line[0]))
                            {
                                inHeader = false;
                                dataLines.Add(line);
                            }
                        }
                        else
                        {
                            dataLines.Add(line);
                        }
                    }
                }

                // ===================================================================
                // HOY alignment matching ladybug EPW behavior
                // ===================================================================
                // EPW file: first line = Jan 1 Hour 1 (00:00-01:00), 
                //           last line = Dec 31 Hour 24 (23:00-24:00 = next day 0:00)
                // 
                // For point-in-time fields (temp, humidity, wind, pressure, sky cover, etc.):
                //   The last value (Dec 31 24:00 = Jan 1 0:00 of next year) is moved to index 0.
                //   This makes HOY=0 correspond to Jan 1 0:00 (matching ladybug hourly collections).
                //   Reordered index mapping: HOY=0 -> last raw line; HOY>0 -> raw line at HOY-1
                //
                // For radiation/illuminance fields (GHI, DNI, DHI, HIR, etc.):
                //   NO reordering. These represent accumulation over the previous hour.
                //   HOY=0 corresponds to the first EPW record (accumulation during 00:00-01:00).
                // ===================================================================

                int nLines = dataLines.Count;
                if (nLines == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No hourly data found in EPW file.");
                    return;
                }

                // Extract hourly data for requested HOYs
                foreach (int hoy in hoyList)
                {
                    if (hoy < 0 || hoy >= nLines)
                    {
                        // Pad with zeros if HOY exceeds data length
                        tdb.Add(0); tdp.Add(0); rh.Add(0); patm.Add(0);
                        ghi.Add(0); dni.Add(0); dhi.Add(0);
                        extHoriz.Add(0); extDni.Add(0); hir.Add(0);
                        windSpeed.Add(0); windDir.Add(0);
                        totalSky.Add(0); opaqueSky.Add(0);
                        visibility.Add(0); ceiling.Add(0);
                        precipWater.Add(0); aerosol.Add(0);
                        snowDepth.Add(0); albedo.Add(0); precipDepth.Add(0);
                        continue;
                    }

                    // Point-in-time fields: use reordered index (ladybug-compatible)
                    // HOY=0 -> last line (Dec 31 24:00 = Jan 1 0:00)
                    // HOY=k -> line at k-1 (for k > 0)
                    int pitIndex = (hoy == 0) ? nLines - 1 : hoy - 1;

                    // Radiation/illuminance fields: use original index (no reordering)
                    int radIndex = hoy;

                    string pitLine = dataLines[pitIndex];
                    string radLine = dataLines[radIndex];

                    string[] pitParts = pitLine.Split(',');
                    string[] radParts = radLine.Split(',');

                    if (pitParts.Length < 22 || radParts.Length < 22)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"EPW data line at HOY {hoy} has insufficient columns. " +
                            $"PIT line={pitParts.Length}, RAD line={radParts.Length}. Expected at least 22.");
                        continue;
                    }

                    double ParseField(string[] parts, int idx)
                    {
                        if (idx < parts.Length && double.TryParse(parts[idx], System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double val))
                            return val;
                        return 0;
                    }

                    // Point-in-time fields from reordered data (ladybug-compatible hour alignment)
                    tdb.Add(ParseField(pitParts, 6));
                    tdp.Add(ParseField(pitParts, 7));
                    rh.Add(ParseField(pitParts, 8));
                    windDir.Add(ParseField(pitParts, 20));
                    windSpeed.Add(ParseField(pitParts, 21));
                    totalSky.Add(ParseField(pitParts, 22));
                    opaqueSky.Add(ParseField(pitParts, 23));
                    visibility.Add(ParseField(pitParts, 24));
                    ceiling.Add(ParseField(pitParts, 25));
                    precipWater.Add(ParseField(pitParts, 28));
                    aerosol.Add(ParseField(pitParts, 29));
                    snowDepth.Add(ParseField(pitParts, 30));
                    albedo.Add(ParseField(pitParts, 32));
                    precipDepth.Add(ParseField(pitParts, 33));

                    // Non-point-in-time fields from original data (NO hour-reordering)
                    // AtmosphericPressure: ladybug's Pressure datatype has point_in_time=FALSE
                    patm.Add(ParseField(radParts, 9));

                    // Radiation/illuminance fields from original data (NO hour-reordering)
                    extHoriz.Add(ParseField(radParts, 10));
                    extDni.Add(ParseField(radParts, 11));
                    hir.Add(ParseField(radParts, 12));
                    ghi.Add(ParseField(radParts, 13));
                    dni.Add(ParseField(radParts, 14));
                    dhi.Add(ParseField(radParts, 15));
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error reading EPW file: " + ex.Message);
                return;
            }

            // Set outputs
            DA.SetData(0, epwInfo);
            DA.SetDataList(1, tdb);
            DA.SetDataList(2, tdp);
            DA.SetDataList(3, rh);
            DA.SetDataList(4, patm);
            DA.SetDataList(5, ghi);
            DA.SetDataList(6, dni);
            DA.SetDataList(7, dhi);
            DA.SetDataList(8, extHoriz);
            DA.SetDataList(9, extDni);
            DA.SetDataList(10, hir);
            DA.SetDataList(11, windSpeed);
            DA.SetDataList(12, windDir);
            DA.SetDataList(13, totalSky);
            DA.SetDataList(14, opaqueSky);
            DA.SetDataList(15, visibility);
            DA.SetDataList(16, ceiling);
            DA.SetDataList(17, precipWater);
            DA.SetDataList(18, aerosol);
            DA.SetDataList(19, snowDepth);
            DA.SetDataList(20, albedo);
            DA.SetDataList(21, precipDepth);
            DA.SetDataTree(22, groundTempTree);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_extractEPW;
        public override Guid ComponentGuid => new Guid("C5E96AAB-C23E-411A-8E81-42D9EC79DAA9");
    }
}
