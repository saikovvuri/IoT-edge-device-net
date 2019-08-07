using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace SampleModule
{
    public class MessageBody
    {
        [JsonProperty(PropertyName = "asset")]
        public String Asset { get; set; }

        [JsonProperty(PropertyName = "source")]
        public String Source { get; set; }

        [JsonProperty(PropertyName = "events")]
        public List<MessageEvent> Events { get; set; }

    }

    public class MessageEvent
    {
        [JsonProperty(PropertyName = "deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty(PropertyName = "timeStamp")]
        public DateTime TimeStamp { get; set; }

        [JsonProperty(PropertyName = "temperature")]
        public SensorReading Temperature { get; set; }

        [JsonProperty(PropertyName = "pressure")]
        public SensorReading Pressure { get; set; }

        [JsonProperty(PropertyName = "suctionPressure")]
        public SensorReading SuctionPressure { get; set; }

        [JsonProperty(PropertyName = "dischargePressure")]
        public SensorReading DischargePressure { get; set; }

        [JsonProperty(PropertyName = "flow")]
        public SensorReading Flow { get; set; }
    }

    public class SensorReading
    {
        [JsonProperty(PropertyName = "value")]
        public double Value { get; set; }

        [JsonProperty(PropertyName = "units")]
        public string Units { get; set; }

        [JsonProperty(PropertyName = "status")]
        public int Status { get; set; }

        [JsonProperty(PropertyName = "misc")]
        public string Misc { get; set; }
    }
}