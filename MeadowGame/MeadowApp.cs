using Meadow;
using Meadow.Devices;
using Meadow.Foundation.Displays;
using Meadow.Foundation.Graphics;
using Meadow.Foundation.Leds;
using Meadow.Hardware;
using Meadow.Peripherals.Displays;
using Meadow.Peripherals.Leds;
using Meadow.Units;
using System;
using System.Threading.Tasks;

namespace MeadowPaddleCatch
{
    public class MeadowApp : App<F7FeatherV2>
    {
        // ---- hardware ----
        RgbPwmLed onboardLed;
        MicroGraphics gfx;
        int W, H;

        // buttons as digital input ports (wire to GND; use internal pull-ups)
        IDigitalInputPort btnLeft;
        IDigitalInputPort btnRight;

        // ---- game state ----
        readonly Color BgColor = Color.Pink;     // screen background
        readonly Color PaddleColor = Color.Black;
        readonly Color BallColor = Color.White;
        readonly Color TextColor = Color.Black;

        int score = 0;
        int lives = 3;
        bool gameOver = false;

        // paddle
        int paddleW = 44;
        int paddleH = 6;
        int paddleX; // left x
        int paddleY; // top y
        int paddleSpeed = 5;

        // ball
        int ballX, ballY, ballSize = 8;
        double ballSpeed = 2.0; // will increase over time

        Random rng = new Random();

        public override Task Initialize()
        {
            Resolver.Log.Info("Initialize...");

            // LED
            onboardLed = new RgbPwmLed(
                Device.Pins.OnboardLedRed,
                Device.Pins.OnboardLedGreen,
                Device.Pins.OnboardLedBlue,
                CommonType.CommonAnode);
            onboardLed.SetColor(Color.Green);

            // SPI + ST7789 (240x240)
            var spiConfig = new SpiClockConfiguration(
                new Frequency(48_000, Frequency.UnitType.Kilohertz),
                SpiClockConfiguration.Mode.Mode3);

            var spi = Device.CreateSpiBus(
                clock: Device.Pins.SCK,
                copi: Device.Pins.MOSI,
                cipo: Device.Pins.MISO,
                config: spiConfig);

            var lcd = new St7789(
                spiBus: spi,
                chipSelectPin: Device.Pins.D02,
                dcPin: Device.Pins.D01,
                resetPin: Device.Pins.D00,
                width: 240,
                height: 240);

            W = (int)lcd.Width;
            H = (int)lcd.Height;

            gfx = new MicroGraphics(lcd)
            {
                Rotation = RotationType._270Degrees
            };
            gfx.CurrentFont = new Font12x16();

            // -------- Buttons as input ports --------
            // Some Meadow versions use ResistorMode.InternalPullUp (not PullUp)
            // We set InterruptMode.EdgeBoth in case you later want event-driven input.
            btnLeft = Device.CreateDigitalInputPort(
                Device.Pins.D03,
                InterruptMode.EdgeBoth,
                ResistorMode.InternalPullUp);
            btnRight = Device.CreateDigitalInputPort(
                Device.Pins.D04,
                InterruptMode.EdgeBoth,
                ResistorMode.InternalPullUp);

            // initial positions
            paddleX = (W - paddleW) / 2;
            paddleY = H - 20;
            ResetBall();

            return base.Initialize();
        }

        public override Task Run()
        {
            // start game loop
            _ = Task.Run(GameLoopAsync);

            // optional LED color cycle in parallel
            _ = Task.Run(() => LedCycleAsync(TimeSpan.FromMilliseconds(900)));

            return Task.CompletedTask;
        }

        async Task GameLoopAsync()
        {
            var frameDelay = TimeSpan.FromMilliseconds(16); // ~60 FPS

            while (true)
            {
                // --- input ---
                // With internal pull-ups, State == true means HIGH (not pressed).
                // Pressed when the button pulls the line LOW -> !State.
                bool left = !btnLeft.State;
                bool right = !btnRight.State;

                // both pressed = restart when game over
                if (gameOver && left && right)
                {
                    score = 0;
                    lives = 3;
                    ballSpeed = 2.0;
                    gameOver = false;
                    ResetBall();
                }

                if (!gameOver)
                {
                    if (left) paddleX -= paddleSpeed;
                    if (right) paddleX += paddleSpeed;

                    // clamp paddle
                    if (paddleX < 0) paddleX = 0;
                    if (paddleX > W - paddleW) paddleX = W - paddleW;

                    // move ball
                    ballY = (int)(ballY + ballSpeed);

                    // collision with paddle?
                    if (ballY + ballSize >= paddleY &&
                        ballY <= paddleY + paddleH &&
                        ballX + ballSize >= paddleX &&
                        ballX <= paddleX + paddleW)
                    {
                        score++;
                        ballSpeed = Math.Min(8.0, ballSpeed + 0.25); // ramp difficulty
                        FlashLed(Color.Blue, 120);
                        ResetBall();
                    }

                    // missed?
                    if (ballY > H)
                    {
                        lives--;
                        FlashLed(Color.Red, 200);
                        if (lives <= 0)
                        {
                            gameOver = true;
                        }
                        ResetBall();
                    }
                }

                // --- draw ---
                DrawFrame();

                await Task.Delay(frameDelay);
            }
        }

        void DrawFrame()
        {
            // background
            gfx.DrawRectangle(0, 0, W, H, BgColor, true);

            // HUD
            gfx.DrawText(6, 4, $"Score: {score}", TextColor);
            gfx.DrawText(W - 110, 4, $"Lives: {lives}", TextColor);

            if (!gameOver)
            {
                // paddle
                gfx.DrawRectangle(paddleX, paddleY, paddleW, paddleH, PaddleColor, true);

                // ball
                gfx.DrawRectangle(ballX, ballY, ballSize, ballSize, BallColor, true);
            }
            else
            {
                // game over overlay
                gfx.DrawText(W / 2 - 50, H / 2 - 10, "GAME OVER", TextColor);
                gfx.DrawText(18, H / 2 + 18, "Press BOTH buttons to restart", TextColor);
            }

            gfx.Show();
        }

        void ResetBall()
        {
            ballX = rng.Next(10, W - ballSize - 10);
            ballY = 20; // start near top
        }

        async Task LedCycleAsync(TimeSpan step)
        {
            Color[] seq = new[] {
                Color.Green, Color.Yellow, Color.Orange, Color.OrangeRed, Color.Red,
                Color.MediumVioletRed, Color.Purple, Color.Magenta, Color.Pink, Color.Cyan
            };

            int i = 0;
            while (true)
            {
                await onboardLed.StartPulse(seq[i], step / 2);
                await Task.Delay(step);
                i = (i + 1) % seq.Length;
            }
        }

        void FlashLed(Color c, int ms)
        {
            onboardLed.SetColor(c);
            _ = Task.Run(async () =>
            {
                await Task.Delay(ms);
                onboardLed.SetColor(Color.Green);
            });
        }
    }
}

