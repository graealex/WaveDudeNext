{
  description = "WaveDude Next Avalonia application";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" ];
      forEachSystem = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      packages = forEachSystem (pkgs: {
        default = pkgs.buildDotnetModule {
          pname = "wavedude-next";
          version = "0.1.3";
          src = ./.;
          projectFile = "WaveDudeNext.App/WaveDudeNext.App.csproj";
          # Generated from NuGet restore by nix/update-nuget-deps.sh; do not edit by hand.
          nugetDeps = ./nix/nuget-deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_10;
          executables = [ "WaveDudeNext.App" ];
          runtimeDeps = [ pkgs.ocl-icd ];
          passthru.updateScript = pkgs.nix-update-script {
            attrPath = "default";
            extraArgs = [ "--flake" "--no-src" "--version=skip" ];
          };
          nativeBuildInputs = [ pkgs.copyDesktopItems ];
          desktopItems = [ (pkgs.makeDesktopItem {
            name = "wavedude-next";
            desktopName = "WaveDude Next";
            comment = "2D wave-equation simulator";
            exec = "WaveDudeNext.App %f";
            icon = "wavedude-next";
            terminal = false;
            categories = [ "Science" ];
          }) ];
          postInstall = ''
            install -D -m644 ${./WaveDudeNext.App/Assets/wavedudenext.png} "$out/share/icons/hicolor/256x256/apps/wavedude-next.png"
          '';
          meta = {
            mainProgram = "WaveDudeNext.App";
            platforms = [ "x86_64-linux" "aarch64-linux" ];
          };
        };
      });

      devShells = forEachSystem (pkgs: {
        default = pkgs.mkShell {
          packages = [ pkgs.dotnet-sdk_10 pkgs.nuget-to-json pkgs.ocl-icd ];
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
          DOTNET_NOLOGO = "1";
          shellHook = ''
            export LD_LIBRARY_PATH="${pkgs.ocl-icd}/lib''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
          '';
        };
      });
    };
}
