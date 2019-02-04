## Welcome!

This sample demonstrates how to show Mixer chat in your game.

## Getting started
Pre-requisite: You need to have the Mixer SDK because the chat plugin references it. You can get the Mixer Unity SDK here: https://github.com/mixer/interactive-unity-plugin/releases

1. Clone or download this repo and drop the ChatUnity/Assets/MixerChat folder into your project.
2. Follow the instructions in the instructions.txt file.

*Note:* Make sure you are NOT in test stream mode! Otherwise you will get a UACCESS error!

## Known issues
This is a sample and does have some known issues:
* Only works for Win32, x64 in the Editor and Standalone builds
* Can cause Unity and Visual Studio to hang sometimes when setting breakpoints. This is a known issue with Unity and native plugins. The workaround is to set the editor to Experimental (.NET 4.6 Equivalent) in Player Settings.
