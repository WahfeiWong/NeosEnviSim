using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Common.Core
{
    /// <summary>
    /// EPW (EnergyPlus Weather) file parser and data container.
    ///
    /// EPW format specification:
    /// https://energyplus.net/weather-formats
    ///
    /// Header structure:
    /// Line 1: LOCATION
    /// Line 2: DESIGN CONDITIONS
    /// Line 3: TYPICAL/EXTREME PERIODS
    /// Line 4: GROUND TEMPERATURES
    /// Line 5: HOLIDAYS/DAYLIGHT SAVINGS
    /// Line 6: COMMENTS 1
    /// Line 7: COMMENTS 2
    /// Line 8: DATA PERIODS
    /// Line 9+: Hourly data records
    ///
    /// Hour alignment (matching ladybug EPW behavior):
    /// - EPW file runs from Hour=1 (00:00-01:00) to Hour=24 (23:00-24:00) on Dec 31.
    /// - For point-in-time fields (temperature, humidity, wind, pressure, sky cover, etc.),
    ///   the last record (Dec 31 24:00 = Jan 1 0:00 of next year) is moved to index 0.
    ///   This ensures GetHour(0) corresponds to Jan 1 0:00 (matching ladybug's hourly collection).
    /// - For radiation/illuminance fields (GHI, DNI, DHI, HIR, etc.), NO reordering is applied.
    ///   These represent accumulation/average over the previous hour, so GetHour(0) returns
    ///   the EPW file's first record (accumulation during 00:00-01:00 of Jan 1).
    /// </summary>
    public class EPWData
    {
        public string City { get; private set; }
        public string StateProvince { get; private set; }
        public string Country { get; private set; }
        public string Source { get; private set; }
        public string WMO { get; private set; }
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public double TimeZone { get; private set; }
        public double Elevation { get; private set; }
        public int DataPeriods { get; private set; } = 1;
        public int RecordsPerHour { get; private set; } = 1;

        /// <summary>
        /// Time step duration in hours. 1.0 for standard hourly EPW,
        /// 0.5 for 30-minute data, 0.25 for 15-minute data, etc.
        /// </summary>
        public double TimeStepHours => RecordsPerHour >= 1 ? 1.0 / RecordsPerHour : 1.0;

        public List<HourlyRecord> HourlyRecords { get; private set; } = new List<HourlyRecord>();

        /// <summary>Tracks which fields had missing (9999/999999) values during parsing.</summary>
        public List<string> MissingDataWarnings { get; private set; } = new List<string>();

        /// <summary>
        /// Is this a leap year file (8784 hours)?
        /// </summary>
        public bool IsLeapYear => HourlyRecords.Count == 8784;

        public EPWData(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("EPW file not found.", filePath);

            ParseFile(filePath);
        }

        private void ParseFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 9)
                throw new InvalidDataException("EPW file too short (minimum 9 lines required).");

            // Parse header lines
            ParseLocationLine(lines[0]);

            // Dynamically find DATA PERIODS line and parse it
            int dataPeriodsLineIndex = FindDataPeriodsLine(lines);
            if (dataPeriodsLineIndex >= 0)
            {
                ParseDataPeriodsLine(lines[dataPeriodsLineIndex]);
            }

            // Hourly data starts after DATA PERIODS line
            int dataStartIndex = dataPeriodsLineIndex + 1;
            if (dataStartIndex >= lines.Length)
                throw new InvalidDataException("No hourly data found after DATA PERIODS header.");

            ParseHourlyData(lines.Skip(dataStartIndex).ToArray());
        }

        /// <summary>
        /// Find the DATA PERIODS line in the EPW header.
        /// </summary>
        private int FindDataPeriodsLine(string[] lines)
        {
            for (int i = 0; i < Math.Min(lines.Length, 10); i++)
            {
                if (lines[i].StartsWith("DATA PERIODS"))
                    return i;
            }
            // Fallback to line index 7 (0-based, which is the 8th line in 1-based)
            return Math.Min(7, lines.Length - 1);
        }

        private void ParseLocationLine(string line)
        {
            if (!line.StartsWith("LOCATION"))
                throw new InvalidDataException("First line must start with LOCATION.");

            var parts = line.Split(',');
            if (parts.Length < 10)
                throw new InvalidDataException("LOCATION line has insufficient fields.");

            City = parts[1].Trim();
            StateProvince = parts[2].Trim();
            Country = parts[3].Trim();
            Source = parts[4].Trim();
            WMO = parts[5].Trim();

            if (!double.TryParse(parts[6], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double lat))
                throw new InvalidDataException("Invalid latitude in LOCATION line.");
            Latitude = lat;

            if (!double.TryParse(parts[7], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double lon))
                throw new InvalidDataException("Invalid longitude in LOCATION line.");
            Longitude = lon;

            if (!double.TryParse(parts[8], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double tz))
                throw new InvalidDataException("Invalid timezone in LOCATION line.");
            TimeZone = tz;

            if (!double.TryParse(parts[9], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double elev))
                throw new InvalidDataException("Invalid elevation in LOCATION line.");
            Elevation = elev;
        }

        private void ParseDataPeriodsLine(string line)
        {
            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                int.TryParse(parts[1], out int nPeriods);
                DataPeriods = nPeriods;
            }
            if (parts.Length >= 3)
            {
                int.TryParse(parts[2], out int nRecords);
                RecordsPerHour = nRecords;
            }
        }

        /// <summary>
        /// Parses hourly data from EPW file body lines.
        /// 
        /// Hour reordering (ladybug-compatible):
        /// EPW files store data from Hour 1 (00:00-01:00) to Hour 24 (23:00-24:00) on Dec 31.
        /// For point-in-time fields, the last record (Dec 31 24:00 = Jan 1 0:00 next year) is moved
        /// to index 0, so GetHour(0) corresponds to Jan 1 0:00.
        /// For radiation/illuminance fields, NO reordering is applied since they represent
        /// accumulation/average over the previous hour.
        /// </summary>
        private void ParseHourlyData(string[] dataLines)
        {
            int expectedHours = 8760;
            int lineIndex = 0;
            var missingFields = new HashSet<string>();
            var rawRecords = new List<HourlyRecord>();

            // Phase 1: Parse all raw records in file order
            foreach (var line in dataLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 35) continue; // EPW has 35+ fields

                try
                {
                    var record = new HourlyRecord
                    {
                        LineIndex = lineIndex,
                        Year = int.Parse(parts[0]),
                        Month = int.Parse(parts[1]),
                        Day = int.Parse(parts[2]),
                        Hour = int.Parse(parts[3]),    // 1-24 in EPW format
                        Minute = int.Parse(parts[4]),
                        DataSource = parts[5],
                        DryBulbTemperature = ParseOrDefault(parts[6]),
                        DewPointTemperature = ParseOrDefault(parts[7]),
                        RelativeHumidity = ParseOrDefault(parts[8]),
                        AtmosphericPressure = ParseOrDefault(parts[9]),
                        ExtraterrestrialHorizontalRadiation = ParseOrDefault(parts[10]),
                        ExtraterrestrialDirectNormalRadiation = ParseOrDefault(parts[11]),
                        HorizontalInfraredRadiation = ParseOrDefaultTracked(parts[12], "HorizontalInfraredRadiation", lineIndex, missingFields),
                        GlobalHorizontalRadiation = ParseOrDefaultTracked(parts[13], "GlobalHorizontalRadiation", lineIndex, missingFields),
                        DirectNormalRadiation = ParseOrDefaultTracked(parts[14], "DirectNormalRadiation", lineIndex, missingFields),
                        DiffuseHorizontalRadiation = ParseOrDefaultTracked(parts[15], "DiffuseHorizontalRadiation", lineIndex, missingFields),
                        GlobalHorizontalIlluminance = ParseOrDefault(parts[16]),
                        DirectNormalIlluminance = ParseOrDefault(parts[17]),
                        DiffuseHorizontalIlluminance = ParseOrDefault(parts[18]),
                        ZenithLuminance = ParseOrDefault(parts[19]),
                        WindDirection = ParseOrDefault(parts[20]),
                        WindSpeed = ParseOrDefault(parts[21]),
                        TotalSkyCover = ParseOrDefault(parts[22]),
                        OpaqueSkyCover = ParseOrDefault(parts[23]),
                        Visibility = ParseOrDefault(parts[24]),
                        CeilingHeight = ParseOrDefault(parts[25]),
                        PresentWeatherObservation = ParseOrDefault(parts[26]),
                        PresentWeatherCodes = ParseOrDefault(parts[27]),
                        PrecipitableWater = ParseOrDefault(parts[28]),
                        AerosolOpticalDepth = ParseOrDefault(parts[29]),
                        SnowDepth = ParseOrDefault(parts[30]),
                        DaysSinceLastSnowfall = ParseOrDefault(parts[31]),
                        Albedo = ParseOrDefault(parts[32]),
                        LiquidPrecipitationDepth = ParseOrDefault(parts[33]),
                        LiquidPrecipitationQuantity = ParseOrDefault(parts[34])
                    };

                    rawRecords.Add(record);
                    lineIndex++;
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            // Validate record count
            if (rawRecords.Count != expectedHours && rawRecords.Count != 8784)
            {
                throw new InvalidDataException(
                    $"EPW file has {rawRecords.Count} hourly records. Expected {expectedHours} or 8784 (leap year).");
            }

            // Phase 2: Apply hour reordering for point-in-time fields (ladybug-compatible)
            // EPW data runs from Hour 1 (00:00-01:00) to Hour 24 (23:00-24:00 = 0:00 next day).
            // For point-in-time fields, the last record (Dec 31 24:00) is moved to index 0,
            // so GetHour(0) corresponds to Jan 1 0:00.
            // For radiation/illuminance fields, NO reordering (they are accumulation over previous hour).
            ApplyHourReordering(rawRecords);
        }

        /// <summary>
        /// Applies ladybug-compatible hour reordering to parsed EPW records.
        ///
        /// Point-in-time fields (temperature, humidity, wind, sky cover, etc.):
        ///   - The last raw record (Dec 31 Hour 24 = Jan 1 0:00 next year) is moved to index 0.
        ///   - This aligns with ladybug EPW's hourly collection where index 0 = Jan 1 0:00.
        ///
        /// Non-point-in-time fields (AtmosphericPressure):
        ///   - AtmosphericPressure is NOT reordered because ladybug's Pressure datatype has
        ///     point_in_time = FALSE. Index 0 = first EPW record.
        ///
        /// Radiation/illuminance fields (GHI, DNI, DHI, HIR, illuminance, zenith luminance):
        ///   - NO reordering. These represent accumulation/average over the previous hour.
        ///   - Index 0 = first EPW record = accumulation during Jan 1 00:00-01:00.
        ///
        /// Time fields (year, month, day, hour, minute, data source):
        ///   - Kept from the ORIGINAL position (no reordering) to preserve the EPW file's datetime.
        /// </summary>
        private void ApplyHourReordering(List<HourlyRecord> rawRecords)
        {
            int n = rawRecords.Count;
            if (n == 0) return;

            HourlyRecords.Clear();
            HourlyRecords.Capacity = n;

            for (int i = 0; i < n; i++)
            {
                // Point-in-time source: last record goes to index 0
                int pitSrcIdx = (i == 0) ? n - 1 : i - 1;
                // Radiation source: original position (no reordering)
                int radSrcIdx = i;
                // Time fields: keep from original position
                int timeSrcIdx = i;

                var pitSrc = rawRecords[pitSrcIdx];
                var radSrc = rawRecords[radSrcIdx];
                var timeSrc = rawRecords[timeSrcIdx];

                var rec = new HourlyRecord
                {
                    LineIndex = i,
                    // Time fields: preserved from original position
                    Year = timeSrc.Year,
                    Month = timeSrc.Month,
                    Day = timeSrc.Day,
                    Hour = timeSrc.Hour,
                    Minute = timeSrc.Minute,
                    DataSource = timeSrc.DataSource,

                    // Point-in-time fields: from reordered source (ladybug-compatible)
                    DryBulbTemperature = pitSrc.DryBulbTemperature,
                    DewPointTemperature = pitSrc.DewPointTemperature,
                    RelativeHumidity = pitSrc.RelativeHumidity,
                    WindDirection = pitSrc.WindDirection,
                    WindSpeed = pitSrc.WindSpeed,
                    TotalSkyCover = pitSrc.TotalSkyCover,
                    OpaqueSkyCover = pitSrc.OpaqueSkyCover,
                    Visibility = pitSrc.Visibility,
                    CeilingHeight = pitSrc.CeilingHeight,
                    PresentWeatherObservation = pitSrc.PresentWeatherObservation,
                    PresentWeatherCodes = pitSrc.PresentWeatherCodes,
                    PrecipitableWater = pitSrc.PrecipitableWater,
                    AerosolOpticalDepth = pitSrc.AerosolOpticalDepth,
                    SnowDepth = pitSrc.SnowDepth,
                    DaysSinceLastSnowfall = pitSrc.DaysSinceLastSnowfall,
                    Albedo = pitSrc.Albedo,
                    LiquidPrecipitationDepth = pitSrc.LiquidPrecipitationDepth,
                    LiquidPrecipitationQuantity = pitSrc.LiquidPrecipitationQuantity,

                    // Non-point-in-time: AtmosphericPressure (ladybug Pressure datatype has point_in_time=FALSE)
                    AtmosphericPressure = radSrc.AtmosphericPressure,

                    // Radiation/illuminance fields: from original source (NO reordering)
                    ExtraterrestrialHorizontalRadiation = radSrc.ExtraterrestrialHorizontalRadiation,
                    ExtraterrestrialDirectNormalRadiation = radSrc.ExtraterrestrialDirectNormalRadiation,
                    HorizontalInfraredRadiation = radSrc.HorizontalInfraredRadiation,
                    GlobalHorizontalRadiation = radSrc.GlobalHorizontalRadiation,
                    DirectNormalRadiation = radSrc.DirectNormalRadiation,
                    DiffuseHorizontalRadiation = radSrc.DiffuseHorizontalRadiation,
                    GlobalHorizontalIlluminance = radSrc.GlobalHorizontalIlluminance,
                    DirectNormalIlluminance = radSrc.DirectNormalIlluminance,
                    DiffuseHorizontalIlluminance = radSrc.DiffuseHorizontalIlluminance,
                    ZenithLuminance = radSrc.ZenithLuminance,
                };

                HourlyRecords.Add(rec);
            }
        }

        private static double ParseOrDefault(string value, double defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            if (value == "9999" || value == "999999") return defaultValue;
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            return defaultValue;
        }

        /// <summary>Non-static version that tracks missing radiation data (9999/999999) for user warnings.</summary>
        private double ParseOrDefaultTracked(string value, string fieldName, int hourIndex, HashSet<string> missingFields)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (value == "9999" || value == "999999")
            {
                // Track the first occurrence of each field with missing data
                if (missingFields.Add(fieldName))
                    MissingDataWarnings.Add($"Hour {hourIndex}: {fieldName} = 9999 (missing), replaced with 0");
                return 0;
            }
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            return 0;
        }

        /// <summary>
        /// Get hourly record by hour-of-year index (0-based).
        /// For leap years, indices range 0-8783.
        /// 
        /// Hour alignment (ladybug-compatible):
        /// - Index 0 = Jan 1, 0:00 (point-in-time fields) or Jan 1 00:00-01:00 accumulation (radiation).
        /// - Temperature, humidity, wind, etc. are aligned to the exact hour.
        /// - Radiation values represent accumulation over the hour preceding the indexed time.
        /// </summary>
        /// <param name="hoy">Hour of year, 0-based (0 = Jan 1, Hour 0)</param>
        /// <returns>Hourly weather record</returns>
        public HourlyRecord GetHour(int hoy)
        {
            if (hoy < 0 || hoy >= HourlyRecords.Count)
                throw new ArgumentOutOfRangeException(nameof(hoy),
                    $"HOY {hoy} out of range [0, {HourlyRecords.Count - 1}]");
            return HourlyRecords[hoy];
        }

        /// <summary>
        /// Convert month/day/hour to hour-of-year (0-based).
        /// Handles leap years correctly.
        /// </summary>
        /// <param name="month">Month (1-12)</param>
        ///<param name = "day" > Day(1 - 31) </ param >
        /// <param name="hour">Hour (0-23)</param>
        /// <returns>Hour of year (0-based)</returns>
        public int DateTimeToHoy(int month, int day, int hour)
        {
            bool isLeap = IsLeapYear;
            int[] daysInMonth = isLeap
                ? new int[] { 31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }
                : new int[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month));
            if (day < 1 || day > daysInMonth[month - 1])
                throw new ArgumentOutOfRangeException(nameof(day));
            if (hour < 0 || hour > 23)
                throw new ArgumentOutOfRangeException(nameof(hour));

            int hoy = 0;
            for (int m = 1; m < month; m++)
                hoy += daysInMonth[m - 1] * 24;
            hoy += (day - 1) * 24;
            hoy += hour;

            return Math.Min(hoy, HourlyRecords.Count - 1);
        }

        /// <summary>
        /// Get number of hours in the EPW file.
        /// </summary>
        public int HourCount => HourlyRecords.Count;
    }

    /// <summary>
    /// Single hour weather record from EPW file.
    /// 
    /// Field alignment:
    /// - Point-in-time fields (temperature, humidity, wind, pressure, sky cover, etc.) are hour-reordered
    ///   to match ladybug EPW: index 0 corresponds to Jan 1 0:00.
    /// - Radiation/illuminance fields are NOT reordered: they represent accumulation over the previous hour.
    ///   Index 0 corresponds to accumulation during Jan 1 00:00-01:00.
    /// </summary>
    public class HourlyRecord
    {
        public int LineIndex;
        public int Year;
        public int Month;
        public int Day;
        public int Hour;        // 1-24 in original EPW; preserved from original position after reordering
        public int Minute;
        public string DataSource;

        // Point-in-time fields (hour-reordered, ladybug-compatible)
        public double DryBulbTemperature;       // C
        public double DewPointTemperature;      // C
        public double RelativeHumidity;         // %
        public double AtmosphericPressure;      // Pa
        public double WindDirection;               // degrees
        public double WindSpeed;                   // m/s
        public double TotalSkyCover;
        public double OpaqueSkyCover;
        public double Visibility;                  // km
        public double CeilingHeight;               // m
        public double PresentWeatherObservation;
        public double PresentWeatherCodes;
        public double PrecipitableWater;           // mm
        public double AerosolOpticalDepth;
        public double SnowDepth;                   // cm
        public double DaysSinceLastSnowfall;
        public double Albedo;
        public double LiquidPrecipitationDepth;    // mm
        public double LiquidPrecipitationQuantity; // hr

        // Radiation fields (NO hour-reordering - accumulation over previous hour)
        public double ExtraterrestrialHorizontalRadiation;  // Wh/m2
        public double ExtraterrestrialDirectNormalRadiation; // Wh/m2
        public double HorizontalInfraredRadiation; // Wh/m2
        public double GlobalHorizontalRadiation;   // Wh/m2
        public double DirectNormalRadiation;       // Wh/m2
        public double DiffuseHorizontalRadiation;  // Wh/m2

        // Illuminance fields (NO hour-reordering - average over previous hour)
        public double GlobalHorizontalIlluminance; // lux
        public double DirectNormalIlluminance;     // lux
        public double DiffuseHorizontalIlluminance;// lux
        public double ZenithLuminance;             // Cd/m2
    }
}
