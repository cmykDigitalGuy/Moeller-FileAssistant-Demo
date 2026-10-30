# Moeller Hub File Assistant — Offline Demo

This repository contains only the fictional-job Windows desktop demonstration. It is separate from private Moeller Printing production repositories and systems.

**Status:** development source. Windows x64 installer builds are published as GitHub Actions artifacts after automated checks. No live API credentials or production services are included.

The application is unsigned; Windows Smart App Control may block it. Do not disable security protections solely for the demo.

## Context menu / compact task windows

Right-click a fictional PDF or PPML ZIP, choose **Show more options** on Windows 11 when necessary, then **Moeller Hub > Assign Artwork / Create Version / Split Quantity / Assign Component / Replace Artwork / More...**. Each direct action opens a narrow, purpose-built panel instead of launching the dashboard. Files outside the fictional workspace are deliberately rejected. The legacy full job dashboard is available only via More.

The public repository excludes downloaded third-party runtime binaries and installer tooling; the first Windows Release build fetches official tools. Downloaded ZIP checks are performed by the build scripts. Run the normal Release x64 solution build to generate the installer ZIP; precompiled signed installers are not provided.
