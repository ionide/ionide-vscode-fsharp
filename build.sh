#!/bin/bash

dotnet tool restore
dotnet run --project build -- $@
