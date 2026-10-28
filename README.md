# FondoClaro

App WPF para Windows 10/11. Mantiene la proporción original al adaptar fondos:

- **Ajustar:** conserva toda la imagen y rellena el espacio con una versión ampliada y suavizada.
- **Rellenar:** ocupa la pantalla y recorta los bordes sobrantes, sin deformar.
- **Archivo de fondo:** prueba el decodificador de imágenes integrado de Windows; si no puede cargar el formato, lo intenta mostrar con WebView2. Puede funcionar con imágenes que Windows decodifique, además de páginas HTML, SVG, PDF, GIF, y medios de audio/video que Edge/WebView2 pueda renderizar.
- **HTML y medios:** muestra el contenido en WebView2 detrás de los iconos del escritorio mientras la app esté abierta. El botón de pausa controla animaciones CSS y etiquetas de video/audio.
- **Capas para HTML:** permite colocar el fondo detrás de las ventanas o al frente cubriendo también la barra de tareas. En el modo al frente, haz clic en la imagen para revelar el botón **Mostrar barra de tareas**; se oculta tras 3 segundos sin movimiento del puntero. Al pulsarlo, el fondo vuelve detrás de las ventanas.
- **Atajo de teclado:** Ctrl + Alt + F11 trae el fondo HTML al frente desde cualquier app. Si otra aplicación ya registró esa combinación, FondoClaro avisa y el atajo no se apropia de ella.
- **Eliminar fondo:** restaura la imagen que estaba puesta antes de usar FondoClaro. Si Windows no reportó una imagen anterior, se solicita el fondo vacío del sistema.
- **Transición de imágenes:** elige dos o más imágenes, cambia entre ellas y mira la cuenta regresiva. El intervalo puede ser manual (segundos, minutos, horas o días) o automático (5 minutos); se puede pausar, reanudar o eliminar la transición conservando el fondo actual.

## Compilar en Windows

1. Instala Visual Studio 2022 con la carga de trabajo **Desarrollo de escritorio de .NET** y el SDK de .NET 8.
2. Abre `src/FondoClaro.csproj` y restaura el paquete NuGet `Microsoft.Web.WebView2`.
3. Ejecuta con F5. Para crear el paquete, ejecuta `./publicar-win-x64.ps1` desde PowerShell en la carpeta del proyecto. También hay un flujo de GitHub Actions que compila y publica un artefacto al subir cambios a `main`.
4. Instala Evergreen WebView2 Runtime en el equipo si no está presente. El runtime permite renderizar HTML y medios compatibles.

## Qué no incluye todavía

- La app aplica imágenes al monitor principal. HTML y medios se extienden al área virtual de monitores conectados. El orden delante/detrás controla la ventana WebView2 mientras la app sigue abierta; los cambios de capa convierten las coordenadas de pantalla a las del escritorio anfitrión.
- La frase “cualquier archivo” está limitada a formatos que el decodificador de Windows o WebView2 puedan leer. RAW de cámara, PSD y formatos propietarios pueden requerir códecs o aplicaciones adicionales.
- La mejora por IA todavía no está incluida. El ajuste de tamaño no necesita IA; una mejora real por IA requiere un modelo local y usa CPU/GPU/RAM mientras trabaja, por lo que no puede ofrecer consumo literalmente cero. Esta versión no procesa nada en segundo plano.
- El fondo HTML o multimedia puede usar CPU/GPU mientras está encendido. **Pausar fondo** detiene animaciones CSS y video/audio; una página que use bucles JavaScript podría seguir ejecutándolos.
- HTML puede contener scripts y recursos externos. Carga solamente archivos y páginas en los que confíes.

## Nota técnica

HTML y formatos multimedia se renderizan con WebView2; la app necesita el runtime de Microsoft Edge WebView2. La imagen se adapta y guarda en `%LocalAppData%\FondoClaro\fondo-adaptado.jpg`, y luego Windows la establece como fondo.

## Linux

Hay un proyecto complementario en `linux/` basado en Avalonia. Aplica imágenes en GNOME, KDE Plasma, Cinnamon, MATE y XFCE. El fondo HTML debajo de iconos se ofrece en X11; Wayland no permite que una app común coloque ventanas arbitrariamente detrás de los iconos. Consulta `linux/README.md` para paquetes y comandos de compilación.
