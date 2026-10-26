namespace Trazo {
    internal static class Program {

        /// <summary>Argumento con el que lo abre el instalador al desinstalar.</summary>
        private const string UninstallArgument = "--desinstalar";

        [STAThread]
        static void Main(string[] args) {
            if (args.Length > 0 && args[0] == UninstallArgument) {
                AskToDeleteSettings();
                return;
            }

            using var mutex = new Mutex(true, "Trazo.SingleInstance", out bool isNew);
            if (!isNew) {
                return;
            }

            // La alta resolución (PerMonitorV2) se configura en App.config
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext());
        }

        /// <summary>Durante la desinstalación: pregunta si borrar también la configuración del usuario.</summary>
        private static void AskToDeleteSettings() {
            Application.EnableVisualStyles();
            // Ventana invisible siempre encima: el mensaje no queda detrás del asistente de desinstalación
            using var owner = new Form { TopMost = true, ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.CenterScreen };
            owner.Show();
            DialogResult answer = MessageBox.Show(owner,
                "¿Quieres borrar también tu configuración de Trazo?\n\n" +
                "Se perderán el atajo de teclado, los colores y el resto de ajustes guardados. " +
                "Si vas a volver a instalar Trazo, elige No para conservarlos.",
                "Desinstalar Trazo", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes) AppSettings.DeleteAll();
        }
    }
}
