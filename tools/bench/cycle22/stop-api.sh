#!/bin/bash
# Stops only the bench API processes (dotnet ServiceBooking.API.dll whose cwd is a /tmp/bench-* worktree).
for p in /proc/[0-9]*; do
  cwd=$(readlink "$p/cwd" 2>/dev/null) || continue
  case "$cwd" in
    /tmp/bench-before/ServiceBooking.API/bin/Release/net8.0|/tmp/bench-after/ServiceBooking.API/bin/Release/net8.0)
      if tr '\0' ' ' < "$p/cmdline" 2>/dev/null | grep -q "ServiceBooking.API.dll"; then kill "${p#/proc/}"; fi ;;
  esac
done
sleep 2
