#!/bin/bash
if test "$OS" = "Windows_NT"
then
  # use .Net

  ./.paket/paket.exe restore
  exit_code=$?
  if [ $exit_code -ne 0 ]; then
  	exit $exit_code
  fi

  packages/build/FAKE/tools/FAKE.exe $@ --nocache --fsiargs build.fsx
else
  # use mono
  mono ./.paket/paket.exe restore
  exit_code=$?
  if [ $exit_code -ne 0 ]; then
  	exit $exit_code
  fi
  mono packages/build/FAKE/tools/FAKE.exe $@ --nocache --fsiargs -d:MONO build.fsx
fi
