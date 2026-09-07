# For Windows testers (beta)

One link, no installers, no .NET download.

## Download

**https://machiilabs.com/winflasher**

Click **Download beta**, save the `.exe`, double-click it.

If Windows SmartScreen warns about an unknown publisher:

1. Click **More info**
2. Click **Run anyway**

(Mach II Labs code signing for Windows is planned; beta builds are unsigned.)

## First test: Connection Probe

1. Plug in the HP programming cable (USB).
2. Open **15CE Flasher**.
3. On the welcome screen, choose **Connection Probe (beta)**.
4. On the calculator: **hold ERASE → press RESET → release ERASE**.
5. Pass: status shows **Connected: ATSAM4LC2C**.

If it stays on “Waiting…”, only FTDI may be visible — repeat ERASE+RESET.

## Requirements

- Windows 10 or 11 (64-bit)
- Official HP 15c CE pogo programming cable
- Nothing else to install (the `.exe` is self-contained)

## Support

Email **support@machiilabs.com** with screenshots of the app window and Device Manager COM ports if something fails.
