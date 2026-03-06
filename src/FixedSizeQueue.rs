#![allow(non_snake_case)]

use std::collections::VecDeque;
use std::sync::Mutex;

pub struct FixedSizeQueue<T> {
    queue: Mutex<VecDeque<T>>,
    capacity: usize,
}

impl<T> FixedSizeQueue<T> {
    pub fn new(capacity: usize) -> Self {
        Self {
            queue: Mutex::new(VecDeque::with_capacity(capacity)),
            capacity,
        }
    }

    pub fn Push(&self, item: T) {
        let mut q = self.queue.lock().unwrap();
        if q.len() >= self.capacity {
            let _ = q.pop_front();
        }
        q.push_back(item);
    }

    pub fn Pop(&self) -> Option<T> {
        self.queue.lock().unwrap().pop_front()
    }

    pub fn TryPop(&self) -> Option<T> {
        self.queue.lock().unwrap().pop_front()
    }

    pub fn Drain(&self) -> Vec<T> {
        self.queue.lock().unwrap().drain(..).collect()
    }

    pub fn Flush(&self) {
        self.queue.lock().unwrap().clear();
    }

    pub fn Empty(&self) -> bool {
        self.queue.lock().unwrap().is_empty()
    }

    pub fn Full(&self) -> bool {
        self.queue.lock().unwrap().len() >= self.capacity
    }

    pub fn Count(&self) -> usize {
        self.queue.lock().unwrap().len()
    }
}
