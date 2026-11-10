// Exemple de bullet per a Ilerda: (768 -> HIDDEN_SIZE)x2 -> 1 amb 8 buckets de sortida per nombre de peces.
// Basat en examples/progression/2_output_buckets.rs de bullet, amb el mateix calendari d'entrenament
// que la xarxa sense buckets (StepLR 0,001 x0,1 cada 7 superbatches, 20 superbatches, WDL 0,75),
// perque l'unic que canvia respecte de la xarxa actual sigui els buckets.
use bullet_lib::{
    game::{inputs::Chess768, outputs::MaterialCount},
    nn::optimiser::AdamW,
    trainer::{
        save::SavedFormat,
        schedule::{TrainingSchedule, TrainingSteps, lr, wdl},
        settings::LocalSettings,
    },
    value::{ValueTrainerBuilder, loader::DirectSequentialDataLoader},
};

const HIDDEN_SIZE: usize = 256;
const NUM_OUTPUT_BUCKETS: usize = 8;
const SCALE: i32 = 400;
const QA: i16 = 255;
const QB: i16 = 64;

fn main() {
    let mut trainer = ValueTrainerBuilder::default()
        .dual_perspective()
        .optimiser(AdamW)
        .inputs(Chess768)
        .output_buckets(MaterialCount::<NUM_OUTPUT_BUCKETS>)
        .save_format(&[
            SavedFormat::id("l0w").round().quantise::<i16>(QA),
            SavedFormat::id("l0b").round().quantise::<i16>(QA),
            // .transpose(): pesos de sortida [bucket][2N], contigus per bucket (el que llegeix Nnue.cs)
            SavedFormat::id("l1w").round().quantise::<i16>(QB).transpose(),
            SavedFormat::id("l1b").round().quantise::<i16>(QA * QB),
        ])
        .loss_fn(|output, target| output.sigmoid().squared_error(target))
        .build(|builder, stm_inputs, ntm_inputs, output_buckets| {
            let l0 = builder.new_affine("l0", 768, HIDDEN_SIZE);
            let l1 = builder.new_affine("l1", 2 * HIDDEN_SIZE, NUM_OUTPUT_BUCKETS);
            let stm_hidden = l0.forward(stm_inputs).screlu();
            let ntm_hidden = l0.forward(ntm_inputs).screlu();
            l1.forward(stm_hidden.concat(ntm_hidden)).select(output_buckets)
        });

    let schedule = TrainingSchedule {
        net_id: "ilerda".to_string(),
        eval_scale: SCALE as f32,
        steps: TrainingSteps { batch_size: 16_384, batches_per_superbatch: 6104, start_superbatch: 1, end_superbatch: 20 },
        wdl_scheduler: wdl::ConstantWDL { value: 0.75 },
        lr_scheduler: lr::StepLR { start: 0.001, gamma: 0.1, step: 7 },
        save_rate: 5,
    };

    let settings = LocalSettings { threads: 4, test_set: None, output_directory: "/kaggle/working/checkpoints", batch_queue_size: 64 };
    let data_loader = DirectSequentialDataLoader::new(&["/kaggle/working/shuf.bullet"]);

    trainer.run(&schedule, &settings, &data_loader);
}
