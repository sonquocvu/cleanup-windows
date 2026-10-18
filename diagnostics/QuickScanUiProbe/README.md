# Quick Scan UI probe

Non-shipping host linking production XAML and ViewModels with fake metadata-only ports and in-memory theme settings. Not part of the production solution.

Start sequence: first fails scope establishment with a private error string that must be redacted; second scans slowly (80 ms per fake entry) for Cancel/navigation/loading checks; third finishes Partial with an unsafe entry; subsequent scans complete. No actual user scope is scanned by this probe.

Build/run with the existing central package pins; no production failure-injection options are added.
