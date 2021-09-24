﻿using LibreHardwareMonitor.Hardware;
using NHM.Common;
using System;
using System.Diagnostics;
using System.Linq;

namespace NHM.DeviceMonitoring
{
    internal class DeviceMonitorCPU : DeviceMonitor, ILoad, ITemp, IGetFanSpeedPercentage
    {
        private PerformanceCounter _cpuCounter { get; set; }
        internal DeviceMonitorCPU(string uuid)
        {
            UUID = uuid;
            _cpuCounter = new PerformanceCounter
            {
                CategoryName = "Processor",
                CounterName = "% Processor Time",
                InstanceName = "_Total"
            };
        }

        public class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }
            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
            }
            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }

        public float Load
        {
            get
            {
                try
                {
                    if (_cpuCounter != null) return _cpuCounter.NextValue();
                }
                catch (Exception e)
                {
                    Logger.ErrorDelayed("CPUDIAG", e.ToString(), TimeSpan.FromMinutes(5));
                }
                return -1;
            }
        }

        public float Temp
        {
            get
            {
                var updateVisitor = new UpdateVisitor();
                 var computer = new Computer();
                 computer.Open();
                 computer.IsCpuEnabled = true;
                 computer.Accept(updateVisitor);
                 var cpu = computer.Hardware.FirstOrDefault(hw => hw.HardwareType == HardwareType.Cpu);
                 var cpuSensors = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature);
                 var cpuSensor = cpuSensors.FirstOrDefault(s => s.Name == "CPU Package" || s.Name.Contains("(Tdie)"));
                 if (cpuSensor == null) cpuSensor = cpuSensors.FirstOrDefault(s => s.Name.Contains("(Tctl/Tdie)"));
                 if (cpuSensor == null) cpuSensor = cpuSensors.FirstOrDefault();
                 if (cpuSensor != null) return Convert.ToInt32(cpuSensor.Value);
                 computer.Close();
                return -1;
            }
        }

        (int status, int percentage) IGetFanSpeedPercentage.GetFanSpeedPercentage()
        {
            var percentage = 0;
            var ok = 0;
            var updateVisitor = new UpdateVisitor();
            var computer = new Computer();
            computer.Open();
            computer.IsMotherboardEnabled = true;
            computer.Accept(updateVisitor);
            var mainboard = computer.Hardware.FirstOrDefault(hw => hw.HardwareType == HardwareType.Motherboard);
            foreach (var subHW in mainboard.SubHardware)
            {
                var groupedSensors = subHW.Sensors
                    .Where(s => (s.SensorType == SensorType.Fan || s.SensorType == SensorType.Control) && s.Value != 0).OrderBy(s => s.Name)
                    .Select(s => new { id = s.Identifier.ToString().Replace("fan", "*").Replace("control", "*"), s })
                    .GroupBy(p => p.id)
                    .Select(g => g.ToArray().Select(p => p.s).OrderBy(s => s.SensorType))
                    .ToArray();

                ISensor sensor = null;
                if (groupedSensors.Any(sg => sg.Count() == 2)) sensor = groupedSensors.FirstOrDefault(sg => sg.Count() == 2).FirstOrDefault(s => s.SensorType == SensorType.Control);

                if (sensor != null) percentage = Convert.ToInt32(sensor.Value);
            }
            computer.Close();
            return (ok, percentage);
        }
    }
}
