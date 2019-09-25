using System.Configuration;

namespace SampleModule
{
    class SimulatorParameters
    {
        public double TempMin { get; set; }

        public double TempMax { get; set; }

        public double PressureMin { get; set; }

        public double PressureMax { get; set; }

        public double AmbientTemp { get; set; }

        public int HumidityPercent { get; set; }

        public static SimulatorParameters Create()
        {
            var appSettings = ConfigurationManager.AppSettings;

            double machineTempMin;
            if (!double.TryParse(appSettings["machineTempMin"], out machineTempMin))
            {
                machineTempMin = 21;
            }

            double machineTempMax;
            if (!double.TryParse(appSettings["machineTempMax"], out machineTempMax))
            {
                machineTempMax = 100;
            }

            double pressureMin;
            if (!double.TryParse(appSettings["machinePressureMin"], out pressureMin))
            {
                pressureMin = 1;
            }

            double pressureMax;
            if (!double.TryParse(appSettings["machinePressureMax"], out pressureMax))
            {
                pressureMax = 10;
            }

            int HumidityPercent;
            if (!int.TryParse(appSettings["ambientHumidity"], out HumidityPercent))
            {
                HumidityPercent = 25;
            }

            return new SimulatorParameters
            {
                TempMin = machineTempMin,
                TempMax = machineTempMax,
                PressureMin = pressureMin,
                PressureMax = pressureMax,
                HumidityPercent = HumidityPercent
            };
        }
    }
}