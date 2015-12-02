@echo off
cls

packages\FAKE\tools\FAKE.exe build.fsx %* --nocache
