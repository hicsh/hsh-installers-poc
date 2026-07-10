BEFORE YOU BEGIN — What this agent does

Requirements

  •  macOS 11 (Big Sur) or later — Apple Silicon or Intel
  •  Any modern browser for the HSH web app

How it works

The HSH Agent runs quietly in the background and exposes a small HTTP + SignalR
endpoint on localhost. The HSH web app connects to it and displays the live data
it streams. Nothing is exposed to the network — the agent only listens on the
loopback interface for your own browser.

Staying up to date

After installing, the agent checks for new versions and updates itself in place.
The web app will also show an "Update now" action when a newer build is available,
so you never have to download an installer by hand again.

Privacy

The agent listens only on localhost and never opens a port to the outside world.
