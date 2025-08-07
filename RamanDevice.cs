using backend_dotnet.Model;
using backend_dotnet.Utils;
using Grpc.Core;
using System.Diagnostics;
using System.IO.Ports;
using System.Management;
using System.Security.AccessControl;
namespace backend_dotnet;

public class RamanDevice
{  
    private bool _is_connected = false;
    private string _device_id = "\"USB\\\\VID_1A86&PID_7523\\\\5&218CC405&0&3\"";
    private int _CCD_PACKET_SIZE = 3648;
    public bool IsConnected
    {
        get { return _is_connected; }
        set { _is_connected = value; }
    }

    private SerialPort _serial_port;
    private Serial _serial;
    private DeviceList _device_list = new DeviceList();
    private Device _device = new Device();
    
    public Device Device
    {
        get { return _device; }
    }

    private readonly ILogger<RamanDevice> _logger;
    public RamanDevice(ILogger<RamanDevice> logger)
    {
        _logger = logger;
        _logger.LogInformation("RamanDevice initialized.");
        _serial_port = new SerialPort();
        _serial = new Serial(logger, _serial_port);
    }

    public DeviceStatus get_status()
    {
        DeviceStatus status = new DeviceStatus();
        status.IsConnected = _is_connected;
        if (_is_connected)
        {
            status.Device = _device;
        }
        return status;
    }
    public DeviceList get_device_list()
    {
        string[] coms = SerialPort.GetPortNames();
        DeviceList devices = new DeviceList();

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
    public bool connect(int index){
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
            bool result = _connect(device.ComPort);
            if (result)
            {
                _device = device;
                _logger.LogInformation("RamanDevice connected.");
                return true;
            }

            _logger.LogError("Device is not connected. Something is wrong.");
            return false;
        }
        catch (Exception ex){
            if (ex is ArgumentOutOfRangeException || ex is NullReferenceException)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Device index={index} is not in list. Check DeviceList again."));

            }
            throw ex;
        }
    }

    private bool _disconnect()
    {
        _serial_port.Close();
        _is_connected = false;
        _device = new Device();
        return true;
    }
    public bool disconnect(){
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
        byte mode = 0;
        //if (acquireMethod == AcquireMethod.HighPrecision)
        //    mode = 0x01;

        double[] ccd_data_temp = _serial.read_ccd(mode);
        return ccd_data_temp;
    }
















}