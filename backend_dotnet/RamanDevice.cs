using backend_dotnet.Model;
using backend_dotnet.Utils;
using Grpc.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
namespace backend_dotnet;

public class RamanDevice
{
    private bool _is_connected = false;
    private readonly int _CCD_PACKET_SIZE = 3648;
    public bool IsConnected
    {
        get { return _is_connected; }
        set { _is_connected = value; }
    }

    private readonly SerialPort _serial_port;
    private readonly Serial _serial;
    private DeviceList _device_list = new();
    private Device _device = new();
    private int _temperature = 0;
    private int _laser_power = 0;
    private int _exposure = 1000;
    public  int _accumulation = 1;
    // private bool is_physical_device = false; 


    public Device Device
    {
        get { return _device; }
    }

    private readonly ILogger<RamanDevice> _logger;
    public RamanDevice(ILogger<RamanDevice> logger)
    {
        _logger = logger;
        string? device_mode = Environment.GetEnvironmentVariable("DEVICE_MODE");
        _logger.LogInformation($"RamanDevice initialized with device_mode={device_mode}.");
        // is_physical_device = device_mode == "mock" ? true : false;
        // if (is_physical_device)
        // {
            _serial_port = new SerialPort();
            _serial = new Serial(logger, _serial_port);
        // }
    }

    public DeviceStatus GetStatus()
    {
        var status = new DeviceStatus()
        { 
            IsConnected = _is_connected
        };
        if (_is_connected)
        {
            status.Device = _device;
            status.LaserPower = _laser_power;
            status.Exposure = _exposure;
            status.Accumulations = _accumulation;
            status.Temperature = get_cool();
        }
        return status;
    }
    public DeviceList GetDeviceList()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var devices = new DeviceList();
        devices.Devices.Add(new Device
        {
            Name = "Mock",
            ComPort = "",
            DeviceId = "mock"
        });
        if (isWindows)
        {
            string[] coms = SerialPort.GetPortNames();

            ManagementObjectCollection usbControllerDeviceCollection = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity").Get();
            foreach (ManagementObject obj in usbControllerDeviceCollection)
            {

                foreach (string com in coms)
                {

                    string name = obj["name"] as string;
                    if (name is not null && name.IndexOf(com) >= 0)
                    {
                        string device_id = obj["DeviceID"] as string;
                        Device device = new Device
                        {
                            Name = name,
                            ComPort = com,
                            DeviceId = device_id
                        };
                        devices.Devices.Add(device);
                        Console.WriteLine($"{device}");
                    }
                }
            }
        }
        _device_list = devices;
        return devices;
    }

    private bool _connect(string portname)
    {
        _serial_port.PortName = portname;
        _serial_port.BaudRate = 115200;
        _serial_port.DataBits = 8;
        _serial_port.Parity = Parity.None;
        _serial_port.StopBits = StopBits.One;
        _serial_port.ReadTimeout = 5000;
        _serial_port.Open();
        _is_connected = true;
        return true;
    }
    public bool connect(int index)
    {
        if (_is_connected)
        {
            _logger.LogWarning("RamanDevice is already connected.");
            return true;
        }
        try
        {
            _logger.LogInformation($"connect to device at index={index}. List={_device_list}");
            Device device = _device_list.Devices.ElementAt(index);
            _logger.LogInformation($"Get Device={device}");
            if (device.Name == "Mock")
            {
                _device = device;
                _logger.LogInformation("[Mocked] RamanDevice connected.");
                _is_connected = true;
                return true;
            }
            else if (_connect(device.ComPort))
            {
                _device = device;
                _logger.LogInformation("RamanDevice connected.");
                return true;
            }

            _logger.LogError("Device is not connected. Something is wrong.");
            return false;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or NullReferenceException)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Device index={index} is not in list. Check DeviceList again."));
        }
    }

    private bool _disconnect()
    {
        _serial_port.Close();
        _is_connected = false;
        _device = new Device();
        return true;
    }
    public bool disconnect()
    {
        if (!_is_connected)
        {
            _logger.LogWarning("RamanDevice is already disconnected.");
            return true;
        }
        bool result = _disconnect();
        _logger.LogInformation("RamanDevice disconnected.");
        return result;
    }

    public double[] read_ccd_data()
    {
        if (!_is_connected) throw new RpcException(new Status(StatusCode.Aborted, $"Device is disconnected"));

        double[] ccd_data_temp = new double[_CCD_PACKET_SIZE];

        if (_device.Name == "Mock")
        {
            Random rnd = new();
            for (int i = 0; i < _CCD_PACKET_SIZE; i++)
            {
                ccd_data_temp[i] = Math.Round(rnd.NextDouble() * 1000,2);
            }
            // sleep 1000 ms
            Thread.Sleep(1000);
        }
        else
        {
            byte mode = 0;
            //if (acquireMethod == AcquireMethod.HighPrecision)
            mode = 0x01;
            _serial.set_laser_power(_laser_power);
            ccd_data_temp = _serial.read_ccd(mode);
        }
        return ccd_data_temp;
    }
    public double[] read_dark_data()
    {
        if (!_is_connected) throw new RpcException(new Status(StatusCode.Aborted, $"Device is disconnected"));

        double[] ccd_data_temp = new double[_CCD_PACKET_SIZE];

        if (_device.Name == "Mock")
        {
            Random rnd = new();
            for (int i = 0; i < _CCD_PACKET_SIZE; i++)
            {
                ccd_data_temp[i] = Math.Round(rnd.NextDouble() * 1000, 2);
            }
            // sleep 1000 ms
            Thread.Sleep(1000);
        }
        else
        {
            _serial.set_laser_power(0);
            ccd_data_temp = _serial.read_dark_ccd();
        }
        return ccd_data_temp;
    }

    public bool set_laser(int laser_power)
    {
        if (!_is_connected) throw new RpcException(new Status(StatusCode.Aborted, $"Device is disconnected"));

        if (laser_power < 0 || laser_power >= 350)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"laser_power must be in [0,150] but got {laser_power}"));
        }

        _laser_power = laser_power;
        _logger.LogInformation($"set_laser = {laser_power}");
        if(_device.Name == "Mock")
        {
            return true;
        }
        else
        {
            if (_serial.set_laser_power(laser_power)) return true;
        }

        // fail to set: fall back to 0
        _laser_power = 0;
        throw new RpcException(new Status(StatusCode.Unknown, "_serial.set_laser_power return false. This should not happen."));

    }
    public bool set_exposure(int exposure)
    {
        if (!_is_connected) throw new RpcException(new Status(StatusCode.Aborted, $"Device is disconnected"));

        if (exposure < 1000)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"exposure must be greater than 1000 but got {exposure}"));
        }

        _exposure = exposure;
        _logger.LogInformation($"set_exposure = {exposure}");
        if (_device.Name == "Mock")
        {
            return true;
        }
        else
        {
            if (_serial.set_exposure(exposure)) return true;
        }

        // fail to set: fall back to 1000
        _exposure = 1000;
        throw new RpcException(new Status(StatusCode.Unknown, "_serial.set_exposure return false. This should not happen."));
    }

    public bool set_accumulation(int accumulation)
    {
        if (!_is_connected) throw new RpcException(new Status(StatusCode.Aborted, $"Device is disconnected"));
        
        _accumulation = accumulation;
        _logger.LogInformation($"set_accumulation = {accumulation}");
        return true;
    }

    public bool set_cool(int temperature)
    {
        if (!_is_connected) throw new RpcException(new Status(StatusCode.Aborted, $"Device is disconnected"));

        if (temperature < -5 || temperature > 25)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"the temperature must be in (-5, 25) but got temperature={temperature}"));
        _temperature = temperature;
        _logger.LogInformation($"set_cool = {temperature}");
        if (_device.Name == "Mock")
        {
            return true;
        }
        else
        {
            if (_serial.set_temperature(temperature)) return true;
        }
        // fail to set: fall back to 0
        _temperature = 0;
        throw new RpcException(new Status(StatusCode.Unknown, "_serial.set_cool return false. This should not happen."));
    }

    public float get_cool()
    {
        if (!_is_connected) throw new RpcException(new Status(StatusCode.Aborted, $"Device is disconnected"));

        var value = 0f;
        string temp = _serial.get_temperature();
        if(!float.TryParse(temp, out value))
        {
            throw new RpcException(new Status(StatusCode.Internal, $"The vaule={temp} is not a float"));
        }
        return value;

    }
}