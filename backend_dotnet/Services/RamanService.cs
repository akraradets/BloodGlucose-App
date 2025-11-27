using backend_dotnet;
using backend_dotnet.Model;
using backend_dotnet.Utils;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace backend_dotnet.Services;

public class RamanService : Raman.RamanBase
{
    private readonly ILogger _logger;
    private readonly RamanDevice _ramanDevice;
    private readonly ATRWrapper _wrapper;
    public RamanService(ILogger<RamanService> logger, RamanDevice ramanDevice, ATRWrapper wrapper)
    {
        _logger = logger;
        _ramanDevice = ramanDevice;
        _wrapper = wrapper;
    }
    public override Task<DeviceList> GetDeviceList(Empty request, ServerCallContext context)
    {
        return Task.FromResult(_ramanDevice.GetDeviceList());
    }

    public override Task<DeviceStatus> DeviceCheck(Empty request, ServerCallContext context)
    {
        return Task.FromResult(_ramanDevice.GetStatus());
    }

    public override Task<DeviceStatus> Connect(ConnectRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Connect: request={request}, index={request.Index}");
        try
        {
            //bool result = _wrapper.OpenDevice("COM10");
            //_logger.LogInformation($"Connect: {result}");
            bool result = _ramanDevice.connect(request.Index);
            if (result == false)
            {
                throw new Exception("OpenDevice Fail");
            }
            return Task.FromResult(_ramanDevice.GetStatus());
        }
        catch (Exception ex) when (!(ex is RpcException))
        {
            throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
        }
    }

    public override Task<DeviceStatus> Disconnect(Empty request, ServerCallContext context)
    {
        _logger.LogInformation($"Disconnect: ");
        try
        {
            bool result = _ramanDevice.disconnect();
            return Task.FromResult(_ramanDevice.GetStatus());
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
        }
    }
    public override async Task ReadCCD(Empty request, IServerStreamWriter<CCD> responseStream, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"Start reading CCD");
            int accum_count = 0;
            while (!context.CancellationToken.IsCancellationRequested && _ramanDevice._accumulation > accum_count)
            //while (!context.CancellationToken.IsCancellationRequested && 1 > accum_count)
            {

                //// each read takes about 400 - 700 ms.
                CCD sample = new CCD();
                //// get dark
                sample.Time = Timestamp.FromDateTimeOffset(DateTimeOffset.Now);
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                //_logger.LogInformation($"--reading dark CCD");
                //_wrapper.SetLdPower(150, 1);
                //_wrapper.SetIntegrationTime(5000);
                //Spectrum spectrum = _wrapper.AcquireSpectrum(AcquireMethod.HighPrecision);
                
                //Spectrum spectrum = _wrapper.AcquireDarkSpectrum();
                double[] ccd_data_dark = _ramanDevice.read_dark_data();

                for (int i = 0; i < 2048; i++)
                {
                    //sample.Data.Add(spectrum.Data[i]);
                    sample.Data.Add(ccd_data_dark[i]);
                }
                stopwatch.Stop();
                sample.Duration = Duration.FromTimeSpan(stopwatch.Elapsed);
                sample.DataType = "dark";
                _logger.LogDebug($"{sample}");
                await responseStream.WriteAsync(sample);

                //// get signal
                sample = new CCD();
                sample.Time = Timestamp.FromDateTimeOffset(DateTimeOffset.Now);
                CCD sample_sub = new CCD();
                sample_sub.Time = Timestamp.FromDateTimeOffset(DateTimeOffset.Now);
                stopwatch = new Stopwatch();
                stopwatch.Start();
                _logger.LogInformation($"--reading signal CCD");
                double[] ccd_data = _ramanDevice.read_ccd_data();
                for (int i = 0; i < 2048; i++)
                {
                    sample.Data.Add(ccd_data[i]);
                    sample_sub.Data.Add(ccd_data[i] - ccd_data_dark[i]);
                }
                stopwatch.Stop();
                sample.Duration = Duration.FromTimeSpan(stopwatch.Elapsed);
                sample_sub.Duration = Duration.FromTimeSpan(stopwatch.Elapsed);
                sample.DataType = "signal";
                sample_sub.DataType = "signal_cal";
                _logger.LogDebug($"{sample}");
                await responseStream.WriteAsync(sample);
                await responseStream.WriteAsync(sample_sub);


                // subtract




                ////await Task.Delay(TimeSpan.FromSeconds(1));
                accum_count++;
            }
        }
        finally
        {
            _logger.LogInformation($"Stop reading CCD");
        }
    }
    public override Task<DeviceStatus> SetMeasureConf(MeasureConfRequest request, ServerCallContext context)
    {

        //_wrapper.SetLdPower(200);
        //_wrapper.SetIntegrationTime(3);
        _logger.LogInformation($"set mesaure config with {request}");
        if (_ramanDevice.set_laser(request.LaserPower) == false)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Something wrong during set_laser. This should not happen"));
        }
        if (_ramanDevice.set_exposure(request.Exposure) == false)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Something wrong during set_exposure. This should not happen"));
        }
        if (_ramanDevice.set_accumulation(request.Accumulations) == false)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Something wrong during set_accumulation. This should not happen"));
        }
        return Task.FromResult(_ramanDevice.GetStatus());
    }
}