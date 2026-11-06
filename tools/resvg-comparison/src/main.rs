use resvg::{tiny_skia, usvg};
use serde_json::{Value, json};
use std::{error::Error, fs, path::Path, sync::Arc};

fn main() -> Result<(), Box<dyn Error>> {
    let args: Vec<String> = std::env::args().collect();
    if args.len() != 3 {
        return Err("expected manifest and new output directory".into());
    }
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
    }
    let report = json!({"renderer":"resvg 0.49.0","rows":rows,"timedExecutions":0,
        "scope":"One optimized process, inputs and explicit font database retained in memory; parse then two raster calls, PNG and I/O separate. No system fonts, child CLI per document, IPC timing or performance claim."});
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
