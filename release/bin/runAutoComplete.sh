#!/bin/bash


exec mono FsAutoComplete.Suave.exe
suavePID = $!

wait $PPID
kill $!