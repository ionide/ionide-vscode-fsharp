#!/bin/bash

dotnet tool restore
dotnet fake build $@
