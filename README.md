# Kinect CMYK PhotoBooth
Interactive photo booth experience using Kinect For Windows v2 and real-time image processing to create a CMYK color separation collage from multiple poses and made immediately available for slideshows and download to mobile devices.

## Background

As one of several experiences designed and developed for an open house party hosted by frog in the Austin studio around SXSW 2016, this installation uses Kinect for Windows for body detection and background removal along with image colorization in real-time which allows users to "paint" themselves in CMYK and download the photo for sharing.

## Dependencies

* Windows 10
* .NET 4.6
* Visual Studio 2015
* Kinect For Windows v2 sensor and SDK
* ImageProcessor (.NET library loaded via NuGet package configuration)
* Display output or projection supporting 1080p (1920 x 1080 resolution)

## Usage

* Building and running the application normally launches the photo booth "input" mode
* Launching the application with a /s switch argument launches the slideshow mode that cycles through the captured and published photos

## Examples

![Kinect CMYK PhotoBooth](images/frog-kinect-cmyk-photobooth-input.jpg)

![Kinect CMYK PhotoBooth](images/frog-kinect-cmyk-photobooth-output-1.png)

![Kinect CMYK PhotoBooth](images/frog-kinect-cmyk-photobooth-output-2.png)