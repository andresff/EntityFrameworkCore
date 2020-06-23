#!/usr/bin/env bash

# This command sends helix test job from local machine

export BUILD_SOURCEBRANCH="master"
export BUILD_REPOSITORY_NAME="efcore"
export SYSTEM_TEAMPROJECT="public"
export BUILD_REASON="test"

if [[ ! -f ".dotnet/dotnet" ]]; then
    dotnet msbuild eng/helix.proj /restore /t:Test "$@"
else
    .dotnet/dotnet msbuild eng/helix.proj /restore /t:Test "$@"
fi