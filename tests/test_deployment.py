import pathlib
import subprocess
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[1]


class DeploymentTests(unittest.TestCase):
    def test_endpoint_redirects_resolve_before_deployment(self):
        with tempfile.TemporaryDirectory(prefix="entra-redirect-test-") as directory:
            workspace = pathlib.Path(directory)
            hive = workspace / "template-hive"
            app = workspace / "app"
            output = workspace / "published"

            def run(*command):
                result = subprocess.run(
                    command, cwd=ROOT, text=True, capture_output=True, timeout=300
                )
                self.assertEqual(
                    result.returncode,
                    0,
                    f"{' '.join(map(str, command))}\n{result.stdout}\n{result.stderr}",
                )
                return result.stdout

            run("dotnet", "new", "install", str(ROOT), "--debug:custom-hive", str(hive))
            run(
                "dotnet", "new", "aspire-entra",
                "--name", "RedirectRegression",
                "--output", str(app),
                "--debug:custom-hive", str(hive),
                "--no-update-check",
            )
            self.assertFalse((app / "tests").exists())
            apphost = app / "RedirectRegression.AppHost/RedirectRegression.AppHost.csproj"

            # Listing deploy steps resolves Entra's Bicep parameters earlier than publish.
            steps = run(
                "aspire", "deploy", "--apphost", str(apphost),
                "--list-steps", "--non-interactive",
            )
            self.assertRegex(
                steps,
                r"provision-entra-web\s*\n\s*[^\n]*Depends on:[^\n]*provision-env",
            )
            run(
                "aspire", "publish", "--apphost", str(apphost),
                "--output-path", str(output), "--non-interactive",
            )

            main = (output / "main.bicep").read_text()
            registration = (output / "entra-web/entra-web.bicep").read_text()
            frontend = (output / "webfrontend/webfrontend.bicep").read_text()
            for index, path in enumerate(("/signin-oidc", "/signout-callback-oidc")):
                self.assertIn(
                    f"redirectUri{index}: 'https://webfrontend."
                    f"${{env.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN}}{path}'",
                    main,
                )
                self.assertIn(f"param redirectUri{index} string", registration)
                self.assertRegex(registration, rf"(?m)^\s+redirectUri{index}\s*$")

            # No dependency on the frontend's deployment output (which needs the registration).
            self.assertNotIn("webfrontend.outputs", main)
            self.assertNotIn("localhost", main)
            self.assertIn("enableIdTokenIssuance: true", registration)
            self.assertIn("external: true", frontend)
            self.assertIn("name: 'Authentication__PublicOrigin'", frontend)
            self.assertIn(
                "value: 'https://webfrontend.${env_outputs_azure_container_apps_environment_default_domain}'",
                frontend,
            )


if __name__ == "__main__":
    unittest.main()
