# MFG application icon

- Editable source asset: `WinUI/Assets/MFG-Enabler.png`.
- Windows icon: `WinUI/Assets/MFG-Enabler.ico`, 16/20/24/32/40/48/64/128/256 px.
- Conversion: `powershell -NoProfile -ExecutionPolicy Bypass -File ./build-icon.ps1`.
- The project embeds the ICO in the EXE and uses it for the window/taskbar. The title bar displays the PNG.
- Created using the built-in image_gen tool. The locally installed NVIDIA App executable's icon was extracted without launching the application, then used as the edit target.
- The extracted original remains in the local cache only and is not included in the deliverable.

## Final generation prompt

Edit target: extracted NVIDIA App icon. Create the replacement Windows app icon requested by the user. Preserve exactly the original square dark charcoal (#202020) background and the placement and size of the bright NVIDIA green rectangular block in the right-middle (approximately x=38% to 83%, y=26% to 74%). Completely erase the NVIDIA eye logo by filling each erased region with its corresponding background color: charcoal on the left and bright green on the right block. Replace it with typography only: one large green capital 'M' on the dark left region, and black capital 'FG' inside the bright green block on the right, forming MFG reading left to right. Lettering uses the bold, squared, condensed, angular futuristic sans-serif style of NVIDIA GeForce RTX wordmarks. M and FG must be clearly legible and optically aligned along the same baseline, fit fully in their respective backgrounds, and stay entirely within the square image. Keep the icon flat, solid-color, crisp, clean, suitable for small Windows taskbar sizes. No eye logo, no NVIDIA word, no additional text, no mockup, no border, no shadows, no gradients, no new background shapes. Output one full-bleed square icon at 1024x1024.
