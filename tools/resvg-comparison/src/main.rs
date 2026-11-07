use resvg::{tiny_skia, usvg};
use serde_json::{Value, json};
use std::{error::Error, fs, hint::black_box, path::Path, sync::Arc, time::Instant};

fn main() -> Result<(), Box<dyn Error>> {
    let args: Vec<String> = std::env::args().collect();
    let timing = match args.as_slice() {
        [_, _, _] => None,
        [_, _, _, flag, warmup, samples] if flag == "--timing" => {
            let counts = (warmup.parse::<usize>()?, samples.parse::<usize>()?);
            if counts.0 == 0 || counts.1 == 0 {
                return Err("warmup and sample counts must be positive".into());
            }
            Some(counts)
        }
        _ => {
            return Err(
                "expected manifest, new output directory and optional --timing warmups samples"
                    .into(),
            );
        }
    };
    let manifest_path = Path::new(&args[1]);
    let directory = manifest_path.parent().ok_or("missing manifest directory")?;
    let output = Path::new(&args[2]);
    if output.exists() {
        return Err("use a new output directory to preserve observations".into());
    }
    fs::create_dir_all(output)?;
    let manifest: Value = serde_json::from_slice(&fs::read(manifest_path)?)?;
    let mut fonts = usvg::fontdb::Database::new();
    for font in manifest["fontPrograms"].as_array().ok_or("missing fonts")? {
        fonts.load_font_data(fs::read(directory.join(text(font, "file")?))?);
    }
    let fonts = Arc::new(fonts);
    // Every SVG and font is in memory before any parse/render operation.
    let inputs: Vec<(&Value, Vec<u8>)> = manifest["cases"]
        .as_array()
        .ok_or("missing cases")?
        .iter()
        .map(|item| Ok((item, fs::read(directory.join(text(item, "input")?))?)))
        .collect::<Result<_, Box<dyn Error>>>()?;
    let mut rows = Vec::new();
    let mut timings = Vec::new();
    for (item, bytes) in inputs {
        let id = text(item, "id")?;
        if Path::new(id).file_name().and_then(|x| x.to_str()) != Some(id) {
            return Err("invalid fixture ID".into());
        }
        if item["scale"].as_f64() != Some(1.0) || text(item, "background")? != "transparent" {
            return Err("expected original unit-scale transparent recipe".into());
        }
        let width = u32::try_from(item["width"].as_u64().ok_or("missing width")?)?;
        let height = u32::try_from(item["height"].as_u64().ok_or("missing height")?)?;
        let mut options = usvg::Options {
            default_size: usvg::Size::from_wh(width as f32, height as f32)
                .ok_or("invalid viewport")?,
            fontdb: Arc::clone(&fonts),
            languages: Vec::new(),
            ..Default::default()
        };
        options.image_href_resolver.resolve_string = Box::new(|_, _| None);
        let tree = usvg::Tree::from_data(&bytes, &options)?;
        // usvg may replace the reported size with drawing bounds after resolving
        // a dimensionless document. Layout already used default_size; keep the
        // caller's viewport as the canvas, without stretching that drawing box.
        let mut pixmap = tiny_skia::Pixmap::new(width, height).ok_or("invalid pixmap")?;
        resvg::render(
            &tree,
            tiny_skia::Transform::identity(),
            &mut pixmap.as_mut(),
        );
        let pixels = pixmap.data().to_vec();
        // Reuse the parsed tree, clearing the destination between operations.
        pixmap.fill(tiny_skia::Color::TRANSPARENT);
        resvg::render(
            &tree,
            tiny_skia::Transform::identity(),
            &mut pixmap.as_mut(),
        );
        if pixels != pixmap.data() {
            return Err("repeated rasterization changed pixels".into());
        }
        let png = pixmap.encode_png()?;
        fs::write(output.join(format!("{id}.png")), png)?;
        rows.push(json!({"id":id,"outcome":"rendered","width":width,"height":height,
            "reportedTreeWidth":tree.size().width(),"reportedTreeHeight":tree.size().height(),"repeatedRasterCalls":2}));
        if let Some((warmups, samples)) = timing {
            // Each operation owns its temporary tree/bitmap/PNG. Font and input
            // acquisition, reference checks and report I/O remain outside timing.
            for mode in ["parse-raster-png", "parse", "prepared-raster", "png"] {
                let operation = || -> Result<(), Box<dyn Error>> {
                    match mode {
                        "parse-raster-png" => {
                            let parsed = usvg::Tree::from_data(black_box(&bytes), &options)?;
                            let mut target =
                                tiny_skia::Pixmap::new(width, height).ok_or("invalid pixmap")?;
                            resvg::render(
                                &parsed,
                                tiny_skia::Transform::identity(),
                                &mut target.as_mut(),
                            );
                            black_box(target.encode_png()?);
                        }
                        "parse" => {
                            black_box(usvg::Tree::from_data(black_box(&bytes), &options)?);
                        }
                        "prepared-raster" => {
                            let mut target =
                                tiny_skia::Pixmap::new(width, height).ok_or("invalid pixmap")?;
                            resvg::render(
                                black_box(&tree),
                                tiny_skia::Transform::identity(),
                                &mut target.as_mut(),
                            );
                            black_box(target);
                        }
                        "png" => {
                            black_box(pixmap.encode_png()?);
                        }
                        _ => unreachable!(),
                    }
                    Ok(())
                };
                let mut warmup_ns = Vec::with_capacity(warmups);
                let mut sample_ns = Vec::with_capacity(samples);
                for _ in 0..warmups {
                    let start = Instant::now();
                    operation()?;
                    warmup_ns.push(u64::try_from(start.elapsed().as_nanos())?);
                }
                for _ in 0..samples {
                    let start = Instant::now();
                    operation()?;
                    sample_ns.push(u64::try_from(start.elapsed().as_nanos())?);
                }
                timings.push(json!({"id":id,"mode":mode,"warmupNanoseconds":warmup_ns,"sampleNanoseconds":sample_ns}));
            }
        }
    }
    let timed_executions =
        timing.map_or(0, |(warmups, samples)| (warmups + samples) * timings.len());
    let report = json!({"renderer":"resvg 0.49.0","rows":rows,"timedExecutions":timed_executions,"timings":timings,
        "scope":"One optimized process, inputs and explicit font database retained in memory; two untimed raster calls verify repeatability before optional serial timing. Timed operations include temporary allocation/destruction. Prepared raster allocates a fresh transparent bitmap; PNG reuses the verified pixels. Font/input acquisition, output I/O, startup and IPC are excluded. All warmups/samples retained; no allocation/native-memory metric or general performance claim."});
    fs::write(
        output.join("report.json"),
        serde_json::to_vec_pretty(&report)?,
    )?;
    println!("{}", serde_json::to_string(&report)?);
    Ok(())
}

fn text<'a>(item: &'a Value, name: &str) -> Result<&'a str, Box<dyn Error>> {
    item[name]
        .as_str()
        .ok_or_else(|| format!("missing {name}").into())
}
