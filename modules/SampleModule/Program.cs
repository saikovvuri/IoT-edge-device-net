namespace SampleModule
{
    using System;
    using System.IO;
    using System.Net;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Shared;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using System.Configuration;
    using Microsoft.Azure.Devices.Edge.ModuleUtil;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Concurrency;

    class Program
    {
        const string MessageCountConfigKey = "MessageCount";
        const string SendDataConfigKey = "SendData";
        const string SendIntervalConfigKey = "SendInterval";
        const string EventCountConfigKey = "EventCount";

        static readonly AtomicBoolean Reset = new AtomicBoolean(false);
        static readonly Guid BatchId = Guid.NewGuid();
        static bool sendData = true;
        static readonly Random Rnd = new Random();
        static TimeSpan messageDelay;

        static int eventCount = 1;

        public enum ControlCommandEnum
        {
            Reset = 0,
            NoOperation = 1
        }
        

        public static int Main() => MainAsync().Result;

        static async Task<int> MainAsync()
        {
            Console.WriteLine("SampleModule Main() started.");
            var appSettings = ConfigurationManager.AppSettings;

            if (!TimeSpan.TryParse(appSettings["MessageDelay"], out messageDelay))
            {
                messageDelay = TimeSpan.FromSeconds(5);
            }

            int messageCount;

            if(!int.TryParse(Environment.GetEnvironmentVariable(MessageCountConfigKey), out messageCount))
            {
                if (!int.TryParse(appSettings[MessageCountConfigKey], out messageCount))
                {
                    messageCount = 500;
                }
            }

            var simulatorParameters = SimulatorParameters.Create();           

            ModuleClient moduleClient = await ModuleUtil.CreateModuleClientAsync(
                TransportType.Amqp_Tcp_Only,
                ModuleUtil.DefaultTimeoutErrorDetectionStrategy,
                ModuleUtil.DefaultTransientRetryStrategy);
            await moduleClient.OpenAsync();
            await moduleClient.SetMethodHandlerAsync("reset", ResetMethod, null);

            (CancellationTokenSource cts, ManualResetEventSlim completed, Option<object> handler) = ShutdownHandler.Init(TimeSpan.FromSeconds(5), null);

            Twin currentTwinProperties = await moduleClient.GetTwinAsync();
            Console.WriteLine("Initialized Twin State Received");

            if (currentTwinProperties.Properties.Desired.Contains(SendIntervalConfigKey))
            {
                Console.WriteLine("SendInterval: " + currentTwinProperties.Properties.Desired[SendIntervalConfigKey]);
                var desiredInterval = (int)currentTwinProperties.Properties.Desired[SendIntervalConfigKey];
                messageDelay = TimeSpan.FromSeconds(desiredInterval);
            }

             if (currentTwinProperties.Properties.Desired.Contains(EventCountConfigKey))
            {
                Console.WriteLine("EventCount: " + currentTwinProperties.Properties.Desired[EventCountConfigKey]);  
                var desiredCount = (int)currentTwinProperties.Properties.Desired[EventCountConfigKey];
                eventCount = desiredCount;
            }

            if (currentTwinProperties.Properties.Desired.Contains(SendDataConfigKey))
            {
                Console.WriteLine("SendData: " + currentTwinProperties.Properties.Desired[SendDataConfigKey]);  
                sendData = (bool)currentTwinProperties.Properties.Desired[SendDataConfigKey];
                if (!sendData)
                {
                    Console.WriteLine("Sending data disabled. Change twin configuration to start sending again.");
                }
            }

            ModuleClient userContext = moduleClient;
            await moduleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdated, userContext);
            await moduleClient.SetInputMessageHandlerAsync("control", ControlMessageHandle, userContext);
            await SendEvents(moduleClient, messageCount, simulatorParameters, cts);
            await cts.Token.WhenCanceled();

            completed.Set();
            handler.ForEach(h => GC.KeepAlive(h));
            Console.WriteLine("SampleModule Main() finished.");
            return 0;
        }

        static Task<MessageResponse> ControlMessageHandle(Message message, object userContext)
        {
            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            Console.WriteLine($"Received message Body: [{messageString}]");

            try
            {
                var messages = JsonConvert.DeserializeObject<ControlCommand[]>(messageString);

                foreach (ControlCommand messageBody in messages)
                {
                    if (messageBody.Command == ControlCommandEnum.Reset)
                    {
                        Console.WriteLine("Resetting temperature sensor..");
                        Reset.Set(true);
                    }
                }
            }
            catch (JsonSerializationException)
            {
                var messageBody = JsonConvert.DeserializeObject<ControlCommand>(messageString);

                if (messageBody.Command == ControlCommandEnum.Reset)
                {
                    Console.WriteLine("Resetting temperature sensor..");
                    Reset.Set(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to deserialize control command with exception: [{ex}]");
            }

            return Task.FromResult(MessageResponse.Completed);
        }

        static bool SendUnlimitedMessages(int maximumNumberOfMessages) => maximumNumberOfMessages < 0;

          static Task<MethodResponse> ResetMethod(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Received direct method call to reset temperature sensor...");
            Reset.Set(true);
            var response = new MethodResponse((int)HttpStatusCode.OK);
            return Task.FromResult(response);
        }

         /// <summary>
        /// Module behavior:
        ///        Sends data periodically (with default frequency of 5 seconds).
        ///        Data trend:
        ///         - Machine Temperature regularly rises from 21C to 100C in regularly with jitter
        ///         - Machine Pressure correlates with Temperature 1 to 10psi
        ///         - Ambient temperature stable around 21C
        ///         - Humidity is stable with tiny jitter around 25%
        ///                Method for resetting the data stream
        /// </summary>
        static async Task SendEvents(
            ModuleClient moduleClient,
            int messageCount,
            SimulatorParameters sim,
            CancellationTokenSource cts)
        {
            int count = 1;
            double currentTemp = sim.TempMin;
            double normal = (sim.PressureMax - sim.PressureMin) / (sim.TempMax - sim.TempMin);

            while (!cts.Token.IsCancellationRequested && (SendUnlimitedMessages(messageCount) || messageCount >= count))
            {
                if (Reset)
                {
                    currentTemp = sim.TempMin;
                    Reset.Set(false);
                }

                if (currentTemp > sim.TempMax)
                {
                    currentTemp += Rnd.NextDouble() - 0.5; // add value between [-0.5..0.5]
                }
                else
                {
                    currentTemp += -0.25 + (Rnd.NextDouble() * 1.5); // add value between [-0.25..1.25] - average +0.5
                }

                if (sendData)
                {
                    var events = new List<MessageEvent>();

                    // Add Desired Number of Events into the Message
                    for (int i = 0; i < eventCount; i++)
                    {
                        events.Add(new MessageEvent
                        {
                            DeviceId = Environment.GetEnvironmentVariable("DEVICE") ?? Environment.MachineName,
                            TimeStamp = DateTime.UtcNow,
                            Temperature = new SensorReading
                            {
                                Value = currentTemp,
                                Units = "degC",
                                Status = 200
                            },
                            Pressure = new SensorReading
                            {
                                Value = sim.PressureMin + ((currentTemp - sim.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            SuctionPressure = new SensorReading
                            {
                                Value = sim.PressureMin + 4 + ((currentTemp - sim.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            DischargePressure = new SensorReading
                            {
                                Value = sim.PressureMin + 1 + ((currentTemp - sim.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            Flow = new SensorReading
                            {
                                Value = Rnd.Next(78, 82),
                                Units = "perc",
                                Status = 200
                            }
                        });
                        currentTemp += -0.25 + (Rnd.NextDouble() * 1.5);
                    }
                   

                    var msgBody = new MessageBody
                    {
                        Asset = Environment.GetEnvironmentVariable("ASSET") ?? "whidbey",
                        Source = Environment.MachineName,
                        Events = events
                    };

                    string dataBuffer = JsonConvert.SerializeObject(msgBody);
                    var eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));
                    eventMessage.Properties.Add("sequenceNumber", count.ToString());
                    eventMessage.Properties.Add("batchId", BatchId.ToString());
                    Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Body: [{dataBuffer}]");

                    await moduleClient.SendEventAsync("temperatureOutput", eventMessage);
                    count++;
                }

                await Task.Delay(messageDelay, cts.Token);
            }

            if (messageCount < count)
            {
                Console.WriteLine($"Done sending {messageCount} messages");
            }
        }

        static async Task OnDesiredPropertiesUpdated(TwinCollection desiredPropertiesPatch, object userContext)
        {
            Console.WriteLine("Device Twin Update Received");

            // At this point just update the configure configuration.
            if (desiredPropertiesPatch.Contains(SendIntervalConfigKey))
            {
                var desiredInterval = (int)desiredPropertiesPatch[SendIntervalConfigKey];
                Console.WriteLine("Updating Send Interval to " + desiredInterval.ToString());
                messageDelay = TimeSpan.FromSeconds(desiredInterval);
            }

            if (desiredPropertiesPatch.Contains(EventCountConfigKey))
            {
                var desiredCount = (int)desiredPropertiesPatch[EventCountConfigKey];
                Console.WriteLine("Updating Event Count to " + desiredCount.ToString());
                eventCount = desiredCount;
            }

            if (desiredPropertiesPatch.Contains(SendDataConfigKey))
            {
                bool desiredSendDataValue = (bool)desiredPropertiesPatch[SendDataConfigKey];
                if (desiredSendDataValue != sendData && !desiredSendDataValue)
                {
                    Console.WriteLine("Turning off Send Data. Change twin configuration to start sending again.");
                }

                sendData = desiredSendDataValue;
            }

            var moduleClient = (ModuleClient)userContext;
            var patch = new TwinCollection($"{{ \"SendData\":{sendData.ToString().ToLower()}, \"SendInterval\": {messageDelay.TotalSeconds}, \"EventCount\": {eventCount.ToString()}}}");
            await moduleClient.UpdateReportedPropertiesAsync(patch); // Just report back last desired property.
        }

        class ControlCommand
        {
            [JsonProperty("command")]
            public ControlCommandEnum Command { get; set; }
        }  

      

        
    }
}
