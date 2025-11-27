# from raman.helper import bold

import numpy as np
from numpy.typing import NDArray
from scipy.interpolate import CubicSpline, interp1d  # type: ignore
from scipy.signal import find_peaks, peak_widths  # type: ignore
from scipy.signal import savgol_filter  # type: ignore
from scipy.signal import butter,filtfilt
from rampy.spectranization import despiking  # type: ignore
from rampy import baseline as rbaseline  # type: ignore
import matplotlib.pyplot as plt

from pathlib import Path
import os
from typing import Self
from datetime import datetime
from copy import deepcopy
from functools import reduce


class Sample:
    """
    `Sample` is a representation of a measurement.

    When two `Samples` have the same Raman Shift (`x`), emulating longer `exposure` can be done with `sample1 + sample2`.
    The result is an object `sample1` with the followings update.
    - (1) `sample1.y` + `sample2.y`
    - (2) `sample1.exposure` + `sample2.exposure`
    - (3) `sample1.paths`.union(`sample2.paths`)

    When two `Samples` have the same Raman Shift (`x`) and same `exposure`, emulating accumulation can be done with `sample1 | sample2`.
    - (1) ( (`sample1.accumulation` * `sample1.y`) + (`sample2.accumulation` * `sample2.y`) ) / (`sample1.accumulation` + `sample2.accumulation`)
    - (2) `sample1.accumulation` + `sample2.accumulation`
    - (3) `sample1.paths`.union(`sample2.paths`)


    Attributes
    ----------
    name : str
        A nme of this `Sample`. Can be any string. By default, it names 'unname'
    x : NDArray of shape (n_samples, )
        A RamanShift of the measurement
    y : NDArray of shape (n_samples, )
        A measured scattering
    paths : set of pathlib.Path
        A set of path where the data is loaded from (if it is loaded from file).
    date : datetime
        A datetime when the data is collected.
    exposure : int
        A time in second to collect the sample.
    accumulation : int
        Number of accumulation done for the sample. Generally, more accumulation is give you better SNR.
    grating : str
        The information which grating is used to collect the sample.
    laser : str
        The information which laser (nm) is used to collect the sample.
    power : float
        The power of the laser used to collect the sample.
    lens : str
        The information which lens is used to collect the sample.
    slit : float
        The slit size used to collect the sample.

    """

    name: str = "unname"
    x: NDArray[np.float64]
    y: NDArray[np.float64]
    paths: set[Path]
    date: datetime
    exposure: int
    accumulation: int
    grating: str
    laser: str
    power: float
    lens: str
    slit: float

    _dx: float

    def __init__(
        self,
        x: NDArray[np.float64],
        y: NDArray[np.float64],
        path: str | Path | None = None,
        interpolate: bool = True,
        verbose: bool = False,
    ):
        if isinstance(x, np.ndarray) == False:  # type: ignore
            raise TypeError(
                f"Expecting `x` to be type of NDArray[np.float64] but got {type(x)}"
            )
        if isinstance(y, np.ndarray) == False:  # type: ignore
            raise TypeError(
                f"Expecting `y` to be type of NDArray[np.float64] but got {type(y)}"
            )
        if x.shape != y.shape:
            raise ValueError(f"shape mismatch between x={x.shape} and y={y.shape}")

        # Original Data that should not be replace so that we can always reset.
        self._x: NDArray[np.float64] = deepcopy(x)
        self._y: NDArray[np.float64] = deepcopy(y)

        self.paths: set[Path] = set({})
        if path:
            self.paths.add(Path(path))

        self.reset_data()
        if interpolate:
            # It is better to remove spike before interpoate
            spike_regions = self.find_spike()
            if len(spike_regions) > 0:
                if verbose:

                    print(f"Found {len(spike_regions)} spike(s) in path={path.as_posix() if path else ''}, self.remove_spike() is perform automatically.")  # type: ignore
                self.remove_spike(auto=False, spike_regions=spike_regions)
            self.interpolate(step=1)

    @property
    def shape(self) -> tuple[int,int]:
        return self.data.shape

    @property
    def data(self) -> np.ndarray:
        return np.hstack([self.x.reshape(-1, 1), self.y.reshape(-1, 1)])

    @property
    def mean(self) -> float:
        return self.y.mean()

    @property
    def std(self) -> float:
        return self.y.std()

    @property
    def stat(self) -> tuple[float, float, float, float]:
        """
        This return a quadruple of (max, min, mean, std) of the sample.y
        """
        return (self.y.max(), self.y.min(), self.mean, self.std)

    def reset_data(self):
        """
        Use to set/reset the data (`x` and `y`) with the original data.
        """
        self.x = deepcopy(self._x)  # type: ignore
        self.y = deepcopy(self._y)  # type: ignore
        self._dx: float = np.diff(self.x).mean()  # type: ignore

    def at(self, shift: float | list[float]) -> np.ndarray:
        """
        Return the sample.y in the range of `shift`.

        Parameters
        ----------
        shift : float or list of float
            The value or values indicate the Raman Shift (sample.x) that you want to look up for the intensity (sample.y)

        Returns
        -------
        NDArray :
            shape of (n_samples, ) of the intensity you look up for.
        """

        # if(  hasattr(self, "_y_interp") == False ):
        #     raise AttributeError(f"The Sample should perform .interpolate first before you can use this method.")
        y_interp = CubicSpline(self.x, self.y, bc_type="natural")
        return y_interp(shift)

    # def find_spike(self, height:float=None, width:float=None, verbose:bool=False) -> list[np.ndarray]:
    def find_spike(
        self, prominence: float = 250, width: float | None = None, verbose: bool = False
    ) -> list[np.ndarray]:
        """
        A wrapper of scipy.signal.find_peaks which return a list of peak index.

        Parameters
        ----------
        prominence : float or None
            The prominence of a peak measures how much a peak stands out from the surrounding baseline
            of the signal and is defined as the vertical distance between the peak and its lowest contour line.
        width : float or None
            The limit of width.
            Default is None, which is calculated to be less than 5 Raman Shift (5/sample._dx)

        Returns
        --------
        list of NDArray :
            Each item in the list is the NDArray with indexes of the peaks region.
        """
        # if( isinstance(height, type(None)) ):
        #     height = self.mean + (4 * self.std)
        if isinstance(width, type(None)):
            width = int(10 / self._dx)

        # peak_idxes, _ = find_peaks(self.y, height=height)
        peak_idxes, _ = find_peaks(self.y, prominence=prominence)
        if verbose:
            print(f"Found {peak_idxes.shape[0]} peaks.")
        widths, _, lefts, rights = peak_widths(self.y, peak_idxes)
        is_spikes = widths < width
        if verbose:
            print(
                f"{is_spikes.sum()}/{peak_idxes.shape[0]} are less than width={width} samples"
            )
            print("id", "width", "left", "right", "is_spike", sep="\t")
            for i in range(len(widths)):
                print(
                    i,
                    round(widths[i], 2),
                    round(lefts[i], 2),
                    round(rights[i], 2),
                    is_spikes[i],
                    sep="\t",
                )

        spike_region: list[np.ndarray] = []
        for left, right in zip(lefts[is_spikes], rights[is_spikes]):

            window = np.arange(np.floor(left) - 1, np.ceil(right) + 1, dtype=np.int64)
            spike_region.append(window)

        return spike_region

    def remove_spike(
        self, auto: bool = True, spike_regions: list[np.ndarray] | None = None
    ):
        """
        Removing Spike based on https://towardsdatascience.com/removing-spikes-from-raman-spectra-a-step-by-step-guide-with-python-b6fd90e8ea77
        Use Spike Region from `Sample.find_spike` then perform a `scipy.interpolate.interp1d(kind='liner')` to corrected the spike.

        Parameters
        ----------
        auto : bool
            if True, `spike_regions` is ignore and use `Sample.find_spike` to find spikes.
            if False, `spike_regions` must be specified.
        spike_regions : list of NDArray or None
            Must be specified is auto is False.
            It is a list of spike (NDArray) indicate the region of spike.
        """
        if auto:
            spike_regions = self.find_spike()
        else:
            if isinstance(spike_regions, type(None)):
                ValueError(f"When auto=False, spike_regions must be specified.")

        if isinstance(spike_regions, type(None)):
            raise ValueError(f"spike_regions is None. This should not happen.")

        for spike_region in spike_regions:
            # create interpolate_window from left and right of the spike_region
            left = spike_region - len(spike_region)
            right = spike_region + len(spike_region)
            interpolate_window = np.concat([left, right])
            # correct the signal with interp1d
            corrector = interp1d(
                interpolate_window, self.y[interpolate_window], kind="linear"
            )
            self.y[spike_region] = corrector(spike_region)

    def despike(self, window_length: str | int = "auto", threshold: int = 3):
        """
        The wrapper of rampy.spectranization.despiking

        Parameters
        ----------
        window_length : str or int
            if 'auto' then the `window_lenght` will be caculate according to the sample._dx to cover 5 Raman Shift.
            The integer specify the size of window for despiking.
        threshold : int
            The threshold that the spike exceed then despike is activiate.
        """
        if isinstance(window_length, str):
            if window_length != "auto":
                raise ValueError(
                    f"window_length should be 'auto' or integer. Got {window_length=}"
                )
            window_length = int(5 / self._dx)
        self.y = despiking(self.x, self.y, neigh=window_length, threshold=threshold)

    def interpolate(self, step: float):
        """
        Use to interpolate with `scipy.interpolate.CubicSpline` the signal.

        Parameters
        ----------
        step : float
            The resolution of the interpolated signal.
        """
        minx = np.floor(self.x.min())
        maxx = np.ceil(self.x.max())
        new_x = np.arange(minx, maxx + step, step=step)
        y_interp = CubicSpline(self.x, self.y, bc_type="natural")
        self.y = y_interp(new_x)
        self.x = new_x
        self._dx = step

    def normalized(self, method: str = "minmax"):
        """
        This will perform normalization on Sample.y

        Parameters
        ----------
        method : str
            'minmax' will use MinMax method to scale Sample.y to [0,1]
            'zscore' will use Z-Score  method to scale Sample.y to mean=0 std=1
        """
        if method == "minmax":
            self.y = (self.y - self.y.min()) / (self.y.max() - self.y.min())
        elif method == "zscore":
            self.y = (self.y - self.mean) / self.std
        else:
            raise ValueError(
                f"method={method} is not supported. Use 'minmax' or 'zscore'. "
            )

    def smoothing(
        self, window_length: str | int = "auto", polyorder=2, test: bool = False
    ) -> np.ndarray:
        """
        This is the wrapper for scipy.signal.savgol_filter

        Paramters
        ---------
        window_lenght : str or int
            if 'auto' then the `window_lenght` will be caculate according to the sample._dx to cover 30 Raman Shift.
            The integer specify the size of window for smoothing.
        polyorder : int
            Default is 2. Specify the polyorder of the filter. The higher the number, less smoothing it is.
        test : bool
            Default is False.
            When this is True, the result of smoothing will no be saved into the sample.y.
        """

        if isinstance(window_length, str):
            if window_length != "auto":
                raise ValueError(
                    f"window_length should be 'auto' or integer. Got {window_length=}"
                )
            window_length = int(30 / self._dx)

        y = deepcopy(self.y)
        y = savgol_filter(x=y, window_length=window_length, polyorder=polyorder)
        if test == False:
            self.y = y
        return y

    def butter_lowpass_filter(self, normal_cutoff:float, order:int=1):
        
        y = deepcopy(self.y)
        # normal_cutoff = cutoff / (fs * 0.5)
        # Get the filter coefficients 
        b, a = butter(order, normal_cutoff, btype='low', analog=False)
        y = filtfilt(b, a, y)
        self.y = y
        return y
    
    def butter_highpass_filter(self, normal_cutoff:float, order:int=1):
        
        y = deepcopy(self.y)
        # normal_cutoff = cutoff / (fs * 0.5)
        # Get the filter coefficients 
        b, a = butter(order, normal_cutoff, btype='high', analog=False)
        y = filtfilt(b, a, y)
        self.y = y
        return y

    ######### Test this #########

    def baseline(
        self, order: int, roi: np.ndarray | None = None, test: bool = False
    ) -> np.ndarray:
        """
        This is the wrapper for rampy.baseline and will only use method='poly'

        Parameters
        ----------
        order : int
            The order of the polynomial to fit the baseline.
        Returns
        -------
        NDArray :
            The baseline of the sample.y
        """
        if order < 1:
            raise ValueError(f"order must be greater than 0. Got {order=}")
        if roi is None:
            roi = self.x

        y = rbaseline(self.x, self.y, method="poly", order=order, roi=roi)
        if test == False:
            self.y = y
        return y

    def extract_range(self, low: float, high: float):
        """
        Use to extract Raman Shift range [low, high]

        Parameters
        ----------
        low : float
            Start of the Raman Shift to extract
        high : float
            End of the Raman Shift to extract
        """
        cond1 = self.x >= low
        cond2 = self.x <= high
        self.x = self.x[cond1 & cond2]
        self.y = self.y[cond1 & cond2]

    def is_same_range(self, sample: Self) -> bool:
        """
        This will check whether the Raman Shift of the input `sample` is the same with this or not.

        Parameters
        ----------
        sample : Sample
            Another instance of `Sample`

        """
        if isinstance(sample, Sample) == False:
            raise TypeError(
                f"sample must be type={type(self)}. sample is type={type(sample)}"
            )
        a = self.x
        b = sample.x

        if a.shape != b.shape:
            return False

        return bool((a == b).all())

    def __radd__(self, b) -> Self:
        return self.__add__(b)

    def __add__(self, b: Self) -> Self:
        if isinstance(b, int):
            new_sample = deepcopy(self)
            new_sample.y += b
            return new_sample

        if isinstance(b, Sample) == False:
            raise TypeError(
                f"Expect a + b to be type={type(self)}. b is type={type(b)}"
            )

        if self.is_same_range(b) == False:
            raise ValueError(f"Expect both a + b to have the same Raman Shift range.")

        new_sample = deepcopy(self)
        new_sample.y += b.y
        new_sample.exposure += b.exposure
        new_sample.paths = new_sample.paths.union(b.paths)
        return new_sample

    def __rmul__(self, b: float) -> Self:
        return self.__mul__(b)

    def __mul__(self, b: float) -> Self:
        if isinstance(b, float):
            new_sample = deepcopy(self)
            new_sample.y *= b
            return new_sample
        else:
            raise TypeError(f"Expect a * b to be type={float}. b is type={type(b)}")

    def __ror__(self, b: Self) -> Self:
        return self.__or__(b)

    def __or__(self, b: Self) -> Self:
        if isinstance(b, Sample) == False:
            raise TypeError(
                f"Expect a | b to be type={type(self)}. b is type={type(b)}"
            )

        if self.is_same_range(b) == False:
            raise ValueError(f"Expect both a | b to have the same Raman Shift range.")

        if self.exposure != b.exposure:
            raise ValueError(f"Expect both a | b to have the same exposure.")

        new_sample = deepcopy(self)
        acc1 = self.accumulation
        acc2 = b.accumulation
        y1 = acc1 * self.y
        y2 = acc2 * b.y
        new_sample.y = (y1 + y2) / (acc1 + acc2)
        new_sample.accumulation = acc1 + acc2
        new_sample.paths = new_sample.paths.union(b.paths)
        return new_sample

    def save(self, path: Path | None = None, basepath: Path = Path()):
        if basepath.exists() == False:
            raise FileExistsError(f"basepath={basepath.as_posix()} is not exists.")
        power_str = str(self.power).replace(".", "-")
        filename: str = (
            f"{self.name}_{self.lens}_{power_str}_{self.grating}_{self.laser}_{self.exposure} s_{self.accumulation}_{self.date.strftime('%Y_%m_%d_%H_%M_%S')}_01.txt"
        )
        if isinstance(path, type(None)):
            path: Path = Path(self.name, self.lens, "sample")  # type: ignore

        path: Path = basepath.joinpath(str(path))  # type: ignore
        os.makedirs(path, exist_ok=True)  # type: ignore
        target: Path = path.joinpath(filename)  # type: ignore
        np.savetxt(target, np.flip(self.data, axis=0))
        print(f"File save to path={target.as_posix()}")

    def plot(self, label: str | None = None, color=None):
        if label is None:
            label = self.name
        plt.plot(self.x, self.y, label=label, alpha=0.8, linewidth=0.8, color=color)  # type: ignore

    def __getitem__(self, idx):
        return self.data[idx]

    def __repr__(self) -> str:
        return self.__str__()

    def __str__(self) -> str:
        smax, smin, smean, sstd = self.stat
        rep = f"""
  {bold('Sample')}: {self.name}
    {bold('date')}: {self.date}
 {bold('grating')}: {self.grating}
   {bold('laser')}: {self.laser}
   {bold('power')}: {self.power}
    {bold('lens')}: {self.lens}
    {bold('slit')}: {self.slit}
{bold('exposure')}: {self.exposure} s
    {bold('accu')}: {self.accumulation}
    {bold('stat')}: Max={round(smax,2)} Min={round(smin,2)} Mean={round(smean,2)} Std={round(sstd,2)}
"""
        return rep


##########################################
##########################################
################ METHOD ##################
##########################################
##########################################


def _load_raman_from_txt(path: Path) -> tuple[np.ndarray, np.ndarray]:
    measure: np.ndarray = np.flip(np.genfromtxt(path), axis=0)
    return deepcopy(measure[:, 0]), deepcopy(measure[:, 1])


def read_txt(
    path: str | Path,
    name_format: list[str] = [
        "name",
        "lens",
        "power",
        "grating",
        "laser",
        "exposure",
        "accumulation",
        "year",
        "month",
        "date",
        "hour",
        "minute",
        "second",
        "01",
    ],
    interpolate: bool = True,
    verbose: bool = False,
) -> Sample:
    """
    Load `Sample` from .txt file exported from Horiba LS6 software

    Parameters
    ----------
    path : str or pathlib.Path
        A path to the .txt file. Could be either `str` or `pathlib.Path`
    name_format : list of str, optional
        A list indicates the naming scheme of the file.
        Default naming scheme is `name_grating_laser_exposure_accumelation_year_month_date_hour_minute_second_01`.
    interpolate : bool
        Default is True.
        This will pass to the Sample(interpolate). It indicates whether you want to perform interpolation during object creation or not.

    Returns
    -------
    Sample
        Object `Sample` is returned.
    """

    # Check if `path` is str
    if isinstance(path, str):
        path: Path = Path(path)  # type: ignore
    # Check if path exist
    if path.exists() == False:  # type: ignore
        raise FileNotFoundError(f"Path={path.as_posix()} is not exist.")  # type: ignore

    # 24_600_785 nm_60 s_1_2024_03_19_10_30_09_01
    # 24_5x_0-71_600_785 nm_60 s_1_2024_03_19_10_30_09_01
    filename: str = os.path.splitext(path.name)[0]  # type: ignore
    values: list[str] = filename.split("_")
    if len(values) != len(name_format):
        raise ValueError(
            f"name_format ({len(name_format)}) is not match the filename ({len(values)}) after split.\nname_format={name_format}.\nfilename={values}"
        )

    x, y = _load_raman_from_txt(path=path)  # type: ignore

    sample = Sample(x=x, y=y, path=path, interpolate=interpolate, verbose=verbose)

    datetime_str: list[str] = []
    for key, value in zip(name_format, values):
        if key in ["year", "month", "date", "hour", "minute", "second"]:
            datetime_str.append(value)
        else:
            if key in ["exposure"]:
                value = int(value.split(" ")[0])  # type: ignore
            elif key in ["power"]:
                value = float(value.replace("-", "."))  # type: ignore
            elif key in ["accumulation"]:
                value = int(value)  # type: ignore
            elif key == "01":
                continue
            sample.__setattr__(key, value)
    sample.__setattr__("date", datetime.strptime("".join(datetime_str), "%Y%m%d%H%M%S"))
    return sample


def accumulate(samples: list[Sample]) -> Sample:
    if isinstance(samples, list) == False:
        raise TypeError(f"Method expect list[Sample] but got {type(samples)}")
    if len(samples) == 1:
        return samples[0]
    return reduce(lambda a, b: a | b, samples)


# if __name__ == '__main__':
#     sample1 = read_txt(path=f"data/silicon/focuspower/silicon-down_600_785 nm_90 s_1_2024_11_19_16_41_27_01.txt")
#     sample2 = read_txt(path=f"data/silicon/focuspower/silicon-down_600_785 nm_60 s_1_2024_11_19_16_33_46_01.txt")
#     sample3 = read_txt(path=f"data/silicon/focuspower/silicon-down_600_785 nm_30 s_1_2024_11_19_16_28_40_01.txt")
#     sample:Sample = sample1 | (sample2 + sample3)
#     print(sample)
#     print(sample1)
#     print(sample2)
#     print(sample3)
#     sample.plot("sample")
#     sample1.plot("sample1")
#     sample2.plot("sample2")
#     sample3.plot("sample3")
#     plt.legend()
#     plt.show()