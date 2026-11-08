# Sources and licenses

All external native libraries are built from their official source repositories. No downloaded third-party DLL or LIB is used.

| Component | Official source | License |
| --- | --- | --- |
| Moonlight Windows base and artwork | https://github.com/moonlight-stream/moonlight-windows | GPL-3.0, see License.txt |
| moonlight-common-c | https://github.com/moonlight-stream/moonlight-common-c | GPL-3.0, see moonlight-common-c/LICENSE.txt |
| ENet, version selected by Moonlight upstream | https://github.com/cgutman/enet | MIT, see moonlight-common-c/enet/LICENSE |
| nanors, version selected by Moonlight upstream | https://github.com/sleepybishop/nanors | See moonlight-common-c/nanors/LICENSE |
| Opus 1.5.2 | https://github.com/xiph/opus | BSD, see third_party/opus/COPYING |
| Mbed TLS 3.6.5 | https://github.com/Mbed-TLS/mbedtls | Apache-2.0 OR GPL-2.0-or-later, see third_party/mbedtls/LICENSE |
| Microsoft UWP runtime 5.2.2 | Microsoft.NETCore.UniversalWindowsPlatform, nuget.org | Microsoft package license |

Exact root revisions are in dependencies.lock.json; nested revisions are the respective upstream gitlinks. Bootstrap checks the root revisions. Two small compatibility patches disable optional desktop DLL loading for UWP thread names and select portable nanors FEC on VS2017 x86/x64 (GFNI intrinsics are unavailable). ARM retains upstream NEON. Protocol and cryptographic algorithms are unchanged.

The application is distributed under GPL-3.0. Preserve the notices and provide corresponding source, submodule revisions and the UWP patch when distributing binaries.
