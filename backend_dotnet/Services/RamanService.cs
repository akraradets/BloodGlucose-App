using backend_dotnet;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace backend_dotnet.Services;

public class RamanService : Raman.RamanBase
{
    private readonly ILogger _logger;
    private readonly RamanDevice _ramanDevice;
    public RamanService(ILogger<RamanService> logger, RamanDevice ramanDevice)
    {
        _logger = logger;
        _ramanDevice = ramanDevice;

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
            bool result = _ramanDevice.connect(request.Index);
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
            {
                // each read takes about 400 - 700 ms.
                CCD sample = new CCD();


                sample.Time = Timestamp.FromDateTimeOffset(DateTimeOffset.Now);
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                double[] ccd_data = _ramanDevice.read_ccd_data();

                for(int i = 0; i < 2048; i++)
                {
                    sample.Data.Add(ccd_data[i]);
                }
                stopwatch.Stop();
                sample.Duration = Duration.FromTimeSpan(stopwatch.Elapsed);

                _logger.LogDebug($"{sample}");
                await responseStream.WriteAsync(sample);
                //await Task.Delay(TimeSpan.FromSeconds(1));
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
        _logger.LogInformation($"set mesaure config with {request}");
        if (_ramanDevice.set_laser(request.LaserPower) == false)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Something wrong during set_laser. This should not happen"));
        }
        if(_ramanDevice.set_exposure(request.Exposure) == false)
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