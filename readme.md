
# PouetRobot

Will download demos for Amiga to your local harddrive from Pouet.net!

Download and build the code with Visual Studio.

Everything is hard coded so you'll need to configure it to your own liking before building and running.

Could easily be changed to download demos for other platforms.

Deleting output folder
When 7-zip unpacks Amiga archives it might create files and folders with names that Windows can't delete correctly. The following command line will be able to delete these folders recursively.
```
rmdir "\\?\D:\Temp\PouetDownload\Output180125_0948" /s
```
